//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using IoFlowNamespace;
using System.Diagnostics;
using System.Threading;

namespace ExampleCacheNamespace
{
    public enum CacheWritePolicy { WriteThrough, WriteBack}; //File cache write policy
    public enum CacheWriteBuffer { Cache, noCache}; //File cache write policy

    /// <summary>
    /// Implements a simple file-based cache. Assumptions: fixed-size access sizes, multiple of sectorSizeBytes
    /// </summary>
    public class FileCache
    {
        public object cacheLock; // lock associated with cache
        public const uint sectorSizeBytes = 512;
        private UInt64 cacheSizeUsedBytes = 0;
        private UInt64 cacheSizeMaxBytes = 0;
        private Boolean noisyOutput = false;
        // we assume accesses are a multiple of ALIGNED_BLOCK_SIZE and also aligned to ALIGNED_BLOCK_SIZE
        // this holds true for TPC-E but is not a general statement
        // XXXET: brittle. Use rangemap eventually
        private uint ALIGNED_BLOCK_SIZE = 4096;

        public UInt64 FileCacheSize { get { return cacheSizeMaxBytes; } set { cacheSizeMaxBytes = value; } }

        public uint AlignedBlockSize { get { return ALIGNED_BLOCK_SIZE; } set { ALIGNED_BLOCK_SIZE = value; }}

        private int CacheProcessID; // processID of this user level cache

        private CacheWritePolicy writePolicy = CacheWritePolicy.WriteThrough;

        private CacheWriteBuffer cacheWrites = CacheWriteBuffer.noCache;

        Thread threadControllerCacheSizeTest;
        public Thread threadPrintStatsTest = null;
        public bool shouldPrintStats = false;


        // A cache is implemented as a collection of hash tables
        // and a list of blocks cached, for LRU/MRU removal
        private Dictionary<uint /* FlowID */,
            Dictionary<string /* file name */, Dictionary<uint /* block id */, FileCacheElement>>> Cache;
        public Dictionary<uint /* FlowID */, FlowSLA> FlowStats;

        public LinkedList<FileCacheElement> freeFileCacheElements;


        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="sizeBytes">Total Cache size in bytes</param>
        /// <param name="noisyOutputIn"></param>
        public FileCache(UInt64 sizeBytes, Boolean noisyOutputIn)
        {
            cacheLock = new object();
            noisyOutput = noisyOutputIn;
            cacheSizeMaxBytes = sizeBytes;
            Cache = new Dictionary<uint /* FlowID */, Dictionary<string /* file name */, Dictionary<uint /* block id */, FileCacheElement>>>();
            FlowStats = new Dictionary<uint /* FlowID */, FlowSLA>();
            CacheProcessID = Process.GetCurrentProcess().Id;
            freeFileCacheElements = new LinkedList<FileCacheElement>();

        }


        private FileCacheElement getFileCacheElement(IoFlow InFlow, string InFileName, byte[] InData, UInt64 InFileOffset, uint copyOffset, uint InDataLength)
        {
            FileCacheElement elemReturn;
            bool reused = false;
            lock (this.freeFileCacheElements)
            {
                if (this.freeFileCacheElements.Count > 0){
                    LinkedListNode<FileCacheElement> llNode = this.freeFileCacheElements.First;
                    this.freeFileCacheElements.Remove(llNode);
                    elemReturn = llNode.Value;
                    reused=true;
                } else {
                    elemReturn = new FileCacheElement(InFlow, InFileName, InData, InFileOffset, copyOffset, InDataLength);
                }
            }
            if (reused)
            {
                Debug.Assert(elemReturn.DataLength == InDataLength);
                elemReturn.fileName = InFileName;
                elemReturn.Flow = InFlow;
                // keep data elemReturn.Data = null;
                elemReturn.FileOffset = InFileOffset;
                elemReturn.DataLength = InDataLength;
                elemReturn.Dirty = false;
                elemReturn.nodeInList = null;
                elemReturn.nodeInDirtyList = null;
                if (InData != null)
                {
                    Buffer.BlockCopy(InData, (int)copyOffset, elemReturn.Data, 0, (int)InDataLength);
                }
            }
            return elemReturn;
        }

        private void returnFreeFileCacheElement(FileCacheElement returnedElem)
        {
            lock (this.freeFileCacheElements)
            {
                if (returnedElem.nodeInFreeFileCacheList != null)
                {
                    this.freeFileCacheElements.AddLast(returnedElem.nodeInFreeFileCacheList);
                }
                else
                {
                    returnedElem.UpdateNodeFreeFileCacheList(this.freeFileCacheElements.AddLast(returnedElem));
                }
            }
        }

        private void cleanUpFreeFileCacheElementList(Int64 blocksToFree)
        {
            lock(this.freeFileCacheElements){
                Int64 blocksToRemove = Math.Min(blocksToFree, this.freeFileCacheElements.Count);
                for (Int64 i = 0; i < blocksToRemove; i++)
                {
                    this.freeFileCacheElements.RemoveFirst();
                }
            }
        }



        private void ControllerCacheSizeTest()
        {
            Thread.Sleep(100000); //sleep until the workload starts up
            uint flowID = this.FlowStats.Keys.ElementAt(0); //assume just one flowID for now

            UInt64[] sizes = { 52428800 /* 50 MB*/, 10485760 /* 10 MB */, 262144000 /* 250 MB */ };

            foreach (UInt64 size in sizes)
            {
                this.CacheSetFlowSize(flowID, size);
                Console.WriteLine("Changed cache size to {0}", size);

                Thread.Sleep(60000); //1 minute
            }
        }


        private void ControllerPrintStatsTest()
        {
            Thread.Sleep(5000);

            while (this.shouldPrintStats)
            {
                Thread.Sleep(5000);
                foreach (FlowSLA flow in this.FlowStats.Values)
                {
                    float realHitRate = (flow.CacheAccessesTotal > 0 ? (float)flow.CacheAccessesHits / (float)flow.CacheAccessesTotal : 0.0F);

                    Console.WriteLine("Flow {0}, {1} bytes , Hits: {2}, Total: {3}, Hit rate: {4}, Evictions: {5}",
                    flow.FlowID, flow.FlowCacheSize, flow.CacheAccessesHits, flow.CacheAccessesTotal, realHitRate, flow.CacheTotalEvictions);
                    flow.CacheAccessesHits = 0;
                    flow.CacheAccessesTotal = 0;
                    flow.CacheTotalEvictions = 0;

                }
            }
        }

        
        public void SetCacheWriteBuffering(CacheWriteBuffer policy){
            this.cacheWrites = policy;
        }

        /// <summary>
        /// Sets the cache write policy
        /// </summary>
        /// <param name="policy"></param>
        public void SetCacheWritePolicy(CacheWritePolicy policy)
        {
            this.writePolicy = policy;
        }


        /// <summary>
        /// Evicts an entry from the cache.  Caller must have locked cache
        /// </summary>
        /// <param name="flowid">flow id of IRP that caused eviction (we evict from same flow)</param>
        private void Evict(uint flowid)
        {
            Dictionary<string, Dictionary<uint, FileCacheElement>> f = null;
            FlowSLA sla = null;
            FileCacheElement ce = null;
            Dictionary<uint, FileCacheElement> b = null;
            FileCacheElement ce2 = null;
            
            uint blockid = 0;
            if (false == Cache.TryGetValue(flowid, out f))
                return;
            if (false == FlowStats.TryGetValue(flowid, out sla))
                return;

            int block_pos = 0;

            // check if I need to evict
            while (sla.FlowCacheSizeUsedBytes > sla.FlowCacheSize && sla.cacheEntries.Count > 0)
            {
                int inner = 0;
                foreach (FileCacheElement node in sla.cacheEntries)
                {
                    ce = node;
                    if (inner == block_pos)
                        break;
                    inner++;
                }

                {
                    lock (ce.LockObj)
                    {



                        if (ce.Data != null)
                        {
                            // real hit
                            if (noisyOutput)
                                Console.WriteLine("Evict {0}: Locked ce on Offset={1} Length={2}",
                                    Thread.CurrentThread.ManagedThreadId, ce.FileOffset, ce.DataLength);
                            Debug.Assert(ce.DataLength == ALIGNED_BLOCK_SIZE);
                            // remove from lookup dictionary
                            blockid = (uint)(ce.FileOffset / ce.DataLength);

                            if (f.TryGetValue(ce.Flow.FileName, out b))
                            {
                                if (b.TryGetValue(blockid, out ce2))
                                {
                                    b.Remove(blockid);
                                    if (b.TryGetValue(blockid, out ce2))
                                    {
                                        Debug.Assert(0 == 1);
                                    }
                                }
                                else
                                {
                                    Debug.Assert(0 == 1);
                                }
                            }
                            else
                            {
                                Debug.Assert(0 == 1);
                            }

                            
                            // remove from linked list
                            Debug.Assert(ce.nodeInList != null);
                            sla.cacheEntries.Remove(ce.nodeInList);

                            returnFreeFileCacheElement(ce);

                            block_pos = 0;
                            cacheSizeUsedBytes -= ce.DataLength;
                            Debug.Assert(cacheSizeUsedBytes >= 0);
                            sla.FlowCacheSizeUsedBytes -= ce.DataLength;
                            sla.CacheTotalEvictions++;

                            //Console.WriteLine("EVICT: {0}", ce.FileOffset);

                            Debug.Assert(sla.FlowCacheSizeUsedBytes >= 0);

                        }
                        else
                        {
                            block_pos++;
                        }
                    }
                }
                                


            }
        }


        /// <summary>
        /// sets the size in Bytes for a flow 
        /// </summary>
        /// <param name="flowID"></param>
        /// <param name="sizeBytes"></param>
        public void CacheSetFlowSize(uint flowID, UInt64 sizeBytes)
        {
            FlowSLA sla = null;
            UInt64 oldFlowCacheSize= 0;

            // XXXET: bit of overkill, might need locks per flow
            lock (cacheLock)
            {
                if (false == FlowStats.TryGetValue(flowID, out sla))
                {
                    sla = new FlowSLA(flowID);
                    FlowStats[flowID] = sla;
                    if (noisyOutput)
                        Console.WriteLine("CacheSetFlowSize flow {0} old NA new {1}", flowID, sizeBytes);
                }
                else
                {
                    if (noisyOutput)
                        Console.WriteLine("CacheSetFlowSize flow {0} old {1} new {2}", flowID, sla.FlowCacheSize, sizeBytes);
                }
                oldFlowCacheSize = sla.FlowCacheSize;
                sla.FlowCacheSize = sizeBytes;

                //Reset the cache counters, since hit rate will be different now
                sla.CacheAccessesHits = 0; 
                sla.CacheAccessesTotal = 0;
                // if cache is shkrinking, need to Evict
                Evict(flowID);
            }
            //Shrink the free file cache element size accordingly
            if (oldFlowCacheSize > sla.FlowCacheSize)
            {
                Debug.Assert(sla.FlowCacheSize % this.AlignedBlockSize == 0);
                Int64 blocksToFree = (Int64)((oldFlowCacheSize - sla.FlowCacheSize) / this.AlignedBlockSize); 

                cleanUpFreeFileCacheElementList(blocksToFree);
            }
        }

        public void CacheCreateGhostCache(uint flowID, UInt32 ioRequestSize)
        {
            FlowSLA sla;
            if (!FlowStats.TryGetValue(flowID, out sla))
            {
                Debug.Assert(0 == 1); //entry should already exist when we call to create the ghost cache...
            }

            const UInt64 ghost_cache_size = 107374182400; //For now start it off with 100GB worth of space

            if (sla.createGhostCache(ioRequestSize, ghost_cache_size, this.noisyOutput))
            {
                if (noisyOutput)
                    Console.WriteLine("CacheCreateGhostCache Created ghost cache for FlowID {0} with IO request size {1}", flowID, ioRequestSize);
            }
            else
            {
                if (noisyOutput)
                    Console.WriteLine("CacheCreateGhostCache FAILED to create ghost cache FlowID {0} with IO request size {1}", flowID, ioRequestSize);
            }
        }

        /// <summary>
        /// Receives a completed IRP, e.g., a completed read
        /// </summary>
        /// <param name="irp"></param>
        /// <returns></returns>
        public PostReadReturnCode CacheIRPCompleted(IRP irp)
        {
            // lookup if IRP is already cached. Assert if so
            Dictionary<string, Dictionary<uint, FileCacheElement>> f = null;
            ulong savedFileOffset = irp.FileOffset;
            uint savedDataLength = irp.DataLength;
            uint dataOffset = irp.DataOffset;
            ulong fileOffset = savedFileOffset;
            Dictionary<uint, FileCacheElement> b = null;
            FileCacheElement ce = null;


            Debug.Assert(irp.IoFlowHeader.MajorFunction == MajorFunction.IRP_MJ_READ);
            

            if (noisyOutput)
                Console.WriteLine("CacheIRPCompleted {0}: Attempting to lock cache on Offset={1} Length={2}",
                    Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

            lock (cacheLock)
            {
                do
                {
                    uint blockid = (uint)(fileOffset / ALIGNED_BLOCK_SIZE);


                    if (noisyOutput)
                        Console.WriteLine("CacheIRPCompleted {0}: Locked cache on Offset={1} Length={2}",
                            Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);


                    if (Cache.TryGetValue(irp.FlowId, out f))
                    {
                        if (f.TryGetValue(irp.IoFlow.FileName, out b))
                        {
                            if (b.TryGetValue(blockid, out ce))
                            {
                                // real hit
                                if (noisyOutput)
                                    Console.WriteLine("CacheIRPCompleted {0}: Attempting to lock ce on Offset={1} Length={2}",
                                        Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

                                lock (ce.LockObj)
                                {
                                    if (noisyOutput)
                                        Console.WriteLine("CacheIRPCompleted {0}: Locked ce on Offset={1} Length={2}",
                                            Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

                                    if (ce.Data != null)
                                    {
                                        if (noisyOutput)
                                            Console.WriteLine("CacheIRPCompleted {0}: Thought we had a miss. Now hit? {1} Length={2}",
                                                Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
                                        //Debug.Assert(0 == 1);
                                    }
                                    //else
                                    //{
                                    ce.UpdateData(irp.GetDataReadOnly(), dataOffset, ALIGNED_BLOCK_SIZE /* copying data */);
                                    ce.Dirty = false;

                                    if (noisyOutput)
                                        Console.WriteLine("CacheIRPCompleted {0}: Waking up all on Offset={1} Length={2}",
                                            Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

                                    // Monitor.PulseAll(ce.LockObj); // wake up anyone waiting on this object
                                    //}
                                }
                                if (noisyOutput)
                                    Console.WriteLine("CacheIRPCompleted {0}: UnLocked ce on Offset={1} Length={2}",
                                        Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
                            }
                        }
                    }

                    if (noisyOutput)
                        Console.WriteLine("CacheIRPCompleted {0}: Cached Offset={1} Length={2}",
                            Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);


                    if (noisyOutput)
                        Console.WriteLine("CacheIRPCompleted {0}: UnLocked cache on Offset={1} Length={2}",
                            Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
                    fileOffset += ALIGNED_BLOCK_SIZE;
                    dataOffset += ALIGNED_BLOCK_SIZE;
                } while (fileOffset < savedFileOffset + savedDataLength);
            }

            return PostReadReturnCode.FLT_POSTOP_FINISHED_PROCESSING;
        }


        /// <summary>
        /// Caches a given IRP on the way down
        /// </summary>
        /// <param name="irp"></param>
        /// <returns></returns>
        public PreReadReturnCode CacheIRP(IRP irp)
        {
            // lookup if IRP is already cached
            Dictionary<string, Dictionary<uint, FileCacheElement>> f = null;

            Dictionary<uint, FileCacheElement> b = null;
            FileCacheElement ce = null;
            bool hit = false;
            bool anyMisses = false;
            FlowSLA sla = null;
            ulong savedFileOffset = irp.FileOffset;
            uint savedDataLength = irp.DataLength;
            uint dataOffset = irp.DataOffset;
            ulong fileOffset = savedFileOffset;
            bool canSatisfyRequest = false;

            Debug.Assert(irp.IoFlowHeader.MajorFunction == MajorFunction.IRP_MJ_READ);

            if (noisyOutput)
                Console.WriteLine("CacheIRP {0}: Attempting to lock cache on Offset={1} Length={2}",
                    Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
            

            Monitor.Enter(cacheLock);
           

            sla = FlowStats[irp.FlowId];
            Debug.Assert(sla != null); //Only deal with explicitly declared flows

            //Do we have enough cache space available to satisfy this request?
            if (sla.FlowCacheSize > irp.DataLength)
            {
                canSatisfyRequest = true;
            }

            if (noisyOutput)
                Console.WriteLine("CacheIRP {0}: Locked cache on Offset={1} Length={2}",
                    Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

            if (canSatisfyRequest)
            {

                
                // iterate over all blocks
                // it's a hit if all blocks are a hit, otherwise its a miss for the entire IRP
                do
                {
                    uint blockid = (uint)(fileOffset / ALIGNED_BLOCK_SIZE);
                    hit = false; //block isn't a hit yet
                    {
                        if (Cache.TryGetValue(irp.FlowId, out f))
                        {
                            if (f.TryGetValue(irp.IoFlow.FileName, out b))
                            {
                                if (b.TryGetValue(blockid, out ce))
                                {
                                    // cache entry exists
                                    if (noisyOutput)
                                        Console.WriteLine("CacheIRP {0}: Attempting to lock ce on Offset={1} Length={2}",
                                            Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
                                    lock (ce.LockObj)
                                    {
                                        if (noisyOutput)
                                            Console.WriteLine("CacheIRP {0}: Locked ce on Offset={1} Length={2}",
                                                Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
                                        if (ce.Data != null)
                                        {
                                            // real hit ; cache entry has data being read
                                            sla = FlowStats[irp.FlowId];
                                            Debug.Assert(sla != null); //We should always have a valid sla entry if we have a hit

                                            hit = true;
                                            byte[] irpData = irp.GetDataReadWrite();
                                            Debug.Assert(ce.DataLength == ALIGNED_BLOCK_SIZE);
                                            Buffer.BlockCopy(ce.Data, 0, irpData, (int)dataOffset, (int)ALIGNED_BLOCK_SIZE);



                                            Debug.Assert(ce.nodeInList != null);
                                            Debug.Assert(ce.nodeInList != null);
                                            sla.cacheEntries.Remove(ce.nodeInList); //Assumes no duplicate ce's in the list, which should be true...
                                            //ce.UpdateNodeList(sla.cacheEntries.AddLast(ce));
                                            sla.cacheEntries.AddLast(ce.nodeInList);


                                            if (sla.FlowSLAHasGhostCache())
                                            {
                                                sla.GhostCache.CacheReadReference(ce.fileName + Convert.ToString(blockid)); //Forward the reference to the ghost cache
                                            }
                                        }
                                        else
                                        {
                                            // cache entry exists, BUT data is still in-flight from storage medium
                                            hit = false;
                                        }
                                    }
                                    if (noisyOutput)
                                        Console.WriteLine("CacheIRP {0}: UnLocked ce on Offset={1} Length={2}",
                                            Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
                                }
                            }
                        }

                        if (!hit)
                        {
                            // evict first
                            Evict(irp.FlowId);

                            // then insert 
                            if (f == null)
                            {
                                Cache[irp.FlowId] = new Dictionary<string, Dictionary<uint, FileCacheElement>>();
                                f = Cache[irp.FlowId];
                            }

                            if (b == null)
                            {
                                f[irp.IoFlow.FileName] = new Dictionary<uint, FileCacheElement>();
                                b = f[irp.IoFlow.FileName];
                            }
                            if (ce == null)
                            {
                                //b[blockid] = new FileCacheElement(irp.IoFlow, irp.IoFlowRuntime.getDriveLetterFileName(irp.IoFlow.FileName), null,
                                //    fileOffset, dataOffset, ALIGNED_BLOCK_SIZE /* copying data only */);
                                //string tempFileNameChunking = irp.IoFlowRuntime.getDriveLetterFileName(irp.IoFlow.FileName); //save file name here once, so we don't do multiple calls to this

                                b[blockid] = getFileCacheElement(irp.IoFlow, irp.IoFlow.FileName, null, fileOffset, dataOffset, ALIGNED_BLOCK_SIZE /* copying data only */);

                                ce = b[blockid];

                                // insert element into list
                                if (false == FlowStats.TryGetValue(irp.FlowId, out sla))
                                {
                                    //sla = new FlowSLA();
                                    //FlowStats[irp.FlowId] = sla;
                                    Debug.Assert(0 == 1); //XXXIS let's only deal with explicitly declared flows right now
                                }
                                ce.UpdateNodeList(sla.cacheEntries.AddLast(ce));
                                cacheSizeUsedBytes += ALIGNED_BLOCK_SIZE;
                                sla.FlowCacheSizeUsedBytes += ALIGNED_BLOCK_SIZE;

                                if (sla.FlowSLAHasGhostCache())
                                {
                                    sla.GhostCache.CacheReadReference(ce.fileName + Convert.ToString(blockid)); //Forward the reference to the ghost cache
                                }
                            }
                        }
                    }
                    if (noisyOutput)
                        Console.WriteLine("CacheIRP {0}: UnLock cache on Offset={1} Length={2}",
                            Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

                    fileOffset += ALIGNED_BLOCK_SIZE;
                    dataOffset += ALIGNED_BLOCK_SIZE;

                    if (hit == false)
                    {
                        anyMisses = true;
                    }
                } while (fileOffset < savedFileOffset + savedDataLength);


                if (false == FlowStats.TryGetValue(irp.FlowId, out sla))
                {
                    Debug.Assert(0 == 1); //XXXIS let's only deal with explicitly declared flows right now
                }
                if (!anyMisses)
                {
                    sla.CacheAccessesHits++; //Increment the number of hits in the cache
                }
            }

            sla.CacheAccessesTotal++; //increment all the accesses to this cache
            sla.FlowBytesAccessed += irp.DataLength; 

            Monitor.Exit(cacheLock);

            // deal with MISSES
            // Let IRP go through and intercept POST operation (with payload) 
            //
            if (anyMisses == true || !canSatisfyRequest)
            {
                //Console.WriteLine("MISS: {0}", irp.FileOffset);
                if (noisyOutput)
                    Console.WriteLine("CacheIRP {0}: PreRead MISS Offset={1} Length={2}",
                        Thread.CurrentThread.ManagedThreadId, irp.FileOffset, irp.DataLength);
                return PreReadReturnCode.FLT_PREOP_SUCCESS_WITH_CALLBACK;
            }
            else
            {
                //Console.WriteLine("HIT: {0}", irp.FileOffset);
                if (noisyOutput)
                    Console.WriteLine("CacheIRP {0}: PreRead HIT Offset={1} Length={2}",
                        Thread.CurrentThread.ManagedThreadId, irp.FileOffset, irp.DataLength);
                return PreReadReturnCode.FLT_PREOP_COMPLETE;
            }
        }


        /// <summary>
        /// Intercepts callback after a write succeeds on write-through
        /// </summary>
        /// <param name="irp"></param>
        /// <returns></returns>
        public PostWriteReturnCode CacheWriteIRPCompleted(IRP irp)
        {
            return PostWriteReturnCode.FLT_POSTOP_FINISHED_PROCESSING;
        }

        /// <summary>
        /// Intercepts a write request, caches it, then sends it down if doing write-through, or returns success and writes it out later if doing write-back
        /// XXXET: shares a lot of functionality with cacheIRP. Probably should be 1 function
        /// </summary>
        /// <param name="irp"></param>
        /// <returns></returns>
        public PreWriteReturnCode CacheWriteIRP(IRP irp)
        {
            Dictionary<string, Dictionary<uint, FileCacheElement>> f = null;
            Dictionary<uint, FileCacheElement> b = null;
            FileCacheElement ce = null;
            FlowSLA sla = null;
            bool blockUpdated = false;
            bool anyBlockUpdated = false;
            ulong savedFileOffset = irp.FileOffset;
            uint savedDataLength = irp.DataLength;
            uint dataOffset = irp.DataOffset;
            ulong fileOffset = savedFileOffset;
            bool writeHit = false;
            bool canSatisfyRequest = false;

            Debug.Assert(irp.IoFlowHeader.MajorFunction == MajorFunction.IRP_MJ_WRITE);


            if ((int)irp.IoFlowHeader.ProcessID != CacheProcessID) //don't intercept traffic we generated
            {

                if (noisyOutput)
                    Console.WriteLine("CacheWriteIRP {0}: Attempting to lock cache on Offset={1} Length={2}",
                        Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
                Monitor.Enter(cacheLock);
                if (noisyOutput)
                    Console.WriteLine("CacheWriteIRP {0}: Locked cache on Offset={1} Length={2}",
                        Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

                //string tempFileNameChunking = irp.IoFlowRuntime.getDriveLetterFileName(irp.IoFlow.FileName); //save file name here once, so we don't do multiple calls to this

                // iterate over all blocks
                // it's a hit if all blocks are a hit, otherwise its a miss for the entire IRP
                do
                {
                    uint blockid = (uint)(fileOffset / ALIGNED_BLOCK_SIZE);

                    //Get the flow stats list
                    if (!FlowStats.TryGetValue(irp.FlowId, out sla))
                    {
                        //sla = new FlowSLA();
                        //FlowStats[irp.FlowId] = sla;
                        Debug.Assert(0 == 1); //XXXIS let's only deal with explicitly declared flows right now
                    }

                    if (!Cache.TryGetValue(irp.FlowId, out f))
                    {
                        Cache[irp.FlowId] = new Dictionary<string, Dictionary<uint, FileCacheElement>>();
                        f = Cache[irp.FlowId];
                    }
                    if (!f.TryGetValue(irp.IoFlow.FileName, out b))
                    {
                        f[irp.IoFlow.FileName] = new Dictionary<uint, FileCacheElement>();
                        b = f[irp.IoFlow.FileName];
                    }

                    if (!b.TryGetValue(blockid, out ce)) // block is not currently cached
                    {
                        if (this.cacheWrites == CacheWriteBuffer.Cache) // only cache the block if write caching is turned on
                        {
                            //b[blockid] = new FileCacheElement(irp.IoFlow, irp.IoFlowRuntime.getDriveLetterFileName(irp.IoFlow.FileName), null,
                            //    fileOffset, dataOffset, ALIGNED_BLOCK_SIZE /* copying data only */);
                            b[blockid] = getFileCacheElement(irp.IoFlow, irp.IoFlow.FileName, null, fileOffset, dataOffset, ALIGNED_BLOCK_SIZE /* copying data only */);

                            ce = b[blockid];

                            //Might need to evict if we don't have enough space for the new entry
                            Evict(irp.FlowId);


                            ce.UpdateNodeList(sla.cacheEntries.AddLast(ce)); //Just create the cache entry
                            cacheSizeUsedBytes += ALIGNED_BLOCK_SIZE;
                            sla.FlowCacheSizeUsedBytes += ALIGNED_BLOCK_SIZE;

                        }
                    }

                    // block is in the cache; only update if the block has data in flight (eg: a read), or if write caching is turned on
                    if ((this.cacheWrites == CacheWriteBuffer.noCache && ce != null) || this.cacheWrites == CacheWriteBuffer.Cache)
                    {

                        {
                            lock (ce.LockObj)
                            {
                                if (noisyOutput)
                                    Console.WriteLine("CacheWriteIRP {0}: Locked ce on Offset={1} Length={2}",
                                        Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);
                                if (noisyOutput)
                                    Console.WriteLine("CacheWriteIRP {0}: Caching write on Offset={1} Length={2}",
                                        Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

                                ce.UpdateData(irp.GetDataReadOnly(), dataOffset, ALIGNED_BLOCK_SIZE /* copying data */);
                                blockUpdated = true;



                                //Move to the front of the LRU list
                                Debug.Assert(ce.nodeInList != null);
                                sla.cacheEntries.Remove(ce.nodeInList);
                                sla.cacheEntries.AddLast(ce.nodeInList);



                                //XXXIS: send all writes to ghost cache
                                if (sla.FlowSLAHasGhostCache())
                                {
                                    sla.GhostCache.CacheReadReference(ce.fileName + Convert.ToString(blockid)); //Forward the reference to the ghost cache
                                }

                            }
                        }

                    }
                    fileOffset += ALIGNED_BLOCK_SIZE;
                    dataOffset += ALIGNED_BLOCK_SIZE;


                    if (blockUpdated == true)
                    {
                        anyBlockUpdated = true;
                    }

                } while (fileOffset < savedFileOffset + savedDataLength);

                
                sla.CacheAccessesTotal++; //update total number of ops that passed through this cache
                sla.FlowBytesAccessed += irp.DataLength;

                if (writeHit)
                {
                    sla.CacheAccessesHits++; 
                }

                Monitor.Exit(cacheLock);
                if (noisyOutput)
                    Console.WriteLine("CacheWriteIRP {0}: UnLocked cache on Offset={1} Length={2}",
                        Thread.CurrentThread.ManagedThreadId, fileOffset, ALIGNED_BLOCK_SIZE);

                if (this.writePolicy == CacheWritePolicy.WriteThrough || anyBlockUpdated == true) //if write-through, or waiting for disk to reply on a read for some block we wrote
                {
                    return PreWriteReturnCode.FLT_PREOP_SUCCESS_WITH_CALLBACK; //send it down
                }
                else
                {
                    Debug.Assert(0 == 1); //we shouldn't be in this case
                }
            }
            return PreWriteReturnCode.FLT_PREOP_COMPLETE; //return complete (ignore traffic we generated)
        }

        public PreCreateReturnCode CachePreCreate(IRP irp)
        {
            return PreCreateReturnCode.FLT_PREOP_SUCCESS_NO_CALLBACK;
        }

        public PreCleanupReturnCode CachePreCleanup(IRP irp)
        {
            return PreCleanupReturnCode.FLT_PREOP_SUCCESS_NO_CALLBACK;
        }

        /// <summary>
        /// Prints stats about the cache
        /// </summary>
        void PrintStats()
        {
        }
    }
}
