//#define USE_GHOST_CACHE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IoFlowAgentNamespace;
using System.Threading;
using OktofsRateControllerNamespace;
using System.Diagnostics;
using IoFlowNamespace;
using System.Net.Sockets;
using ExampleCacheNamespace;

namespace MoiraiSlaveNamespace
{
    class Program
    {
        static void Main(string[] args)
        {
            MoiraiSlave slave = new MoiraiSlave();
            Thread.Sleep(System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>
    /// Moirai slave daemon. Will talk to user level caches.
    /// Speaks to a) remote RateController over socket and b) local IoFlow driver over IoFlowRuntime.
    /// </summary>
    public class MoiraiSlave : IIoFlowAgentClient
    {
        
        Boolean noisyOutput = false;

        FileCache cache = null;
        ManualResetEvent CacheThreadBlocked = new ManualResetEvent(false);

        uint TenantId = 0;
        IoFlowAgent ioFlowAgent = null;
        IoFlowRuntime runtime = new IoFlowRuntime((uint)Environment.ProcessorCount);

        //
        // Keep local state in dictionaries concerning protocol run.
        //
        Dictionary<uint, IoFlowMessageParams> DictFlowCreateParams = null;
        Dictionary<uint, MsgRapArg> DictRapCreateParams = null;
        Dictionary<uint, IoFlow> DictIoFlow = null;
        object LockFlows = new object();

        public MoiraiSlave()
        {
            ioFlowAgent = new IoFlowAgent(this);
            cache = new FileCache(1024 /* bytes test ; not used*/, noisyOutput);
            
            Reset();
            runtime.Start();
        }

        /// <summary>
        /// Helper for tidying up local state and driver state e.g. if rate controller shuts down. 
        /// </summary>
        private void Reset()
        {
            lock (LockFlows)
            {
                runtime.RuntimeReset();
                DictFlowCreateParams = null;
                DictRapCreateParams = null;
                DictIoFlow = new Dictionary<uint, IoFlow>();
                if (this.cache!=null && this.cache.threadPrintStatsTest != null)
                {
                    this.cache.shouldPrintStats = false;
                }
                this.cache = null; //Throws away the entire file cache and all its contents
                GC.Collect();
            }
        }

        #region callbacks IoFlowSlave
        /// <summary>
        /// Callback when slave receives MessageRegister.
        /// </summary>
        /// <param name="tenantId"></param>
        /// <param name="alertVec"></param>
        public void CallbackMessageRegister(Connection conn, uint tenantId, UInt64 alertVec)
        {
            Console.WriteLine("CallbackMessageRegister({0},{1},{1:X8})", conn.RemoteEndPoint, tenantId, alertVec);
            TenantId = tenantId;
        }

        /// <summary>
        /// Callback when slave has received all create parameters for flows and raps from rate controller.
        /// This is a request from the Rate Controller to configure IoFlow diver state for current tenant.
        /// </summary>
        /// <param name="dictFlowParams">Flow create params keyed on FlowId.</param>
        /// <param name="dictRapParams">RAP create params keyed on FlowId.</param>
        /// <returns></returns>
        public OktoResultCodes CallbackIoFlowCreate(
            Dictionary<uint, IoFlowMessageParams> dictFlowParams,
            Dictionary<uint, MsgRapArg> dictRapParams)
        {
            lock (LockFlows)
            {
                DictFlowCreateParams = dictFlowParams;
                DictRapCreateParams = dictRapParams;
                foreach (uint flowId in DictFlowCreateParams.Keys)
                {
                    IoFlowMessageParams flowc = DictFlowCreateParams[flowId];
                    MsgRapArg rapc = DictRapCreateParams[flowId];
                    string[] separators = new string[] { " ", "\t" };
                    string[] toks = flowc.ParameterString.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                    string vmName = toks[1];
                    string volumeName = toks[4];
                    UInt64 flowCacheSize = Convert.ToUInt64(toks[5]);
                    UInt32 ghostCacheBlockSize = Convert.ToUInt32(toks[6]);
                    string writePolicy = toks[7];
                    string writeCachePolicy = toks[8];
                    UInt64 fileCacheTotalSize = Convert.ToUInt64(toks[9]);

                    if (cache == null) //Create a file cache when we get the first request for a flow create
                    {
                        this.cache = new FileCache(fileCacheTotalSize, noisyOutput);
                    }
                    
                    string thisMachine = System.Environment.MachineName.ToLower();
                    string ctrlFileName = rapc.ShareOrVolume;
                    string ctrlHostName = null;
                    string fileName = null;

                    if (ctrlFileName.Substring(0, 2).Equals(@"\\"))
                    {
                        ctrlHostName = ctrlFileName.Substring(2);
                    } 
                    else if (ctrlFileName.Substring(0, 1).Equals(@"\"))
                    {
                        ctrlHostName = ctrlFileName.Substring(1);
                    }
                    else
                    {
                        Debug.Assert(0 == 1); //all strings from the controller should start like that
                    }
                    int idxEndHostName = ctrlHostName.IndexOf(@"\");
                    string shareFile = ctrlHostName.Substring(idxEndHostName);
                    ctrlHostName = ctrlHostName.Substring(0, idxEndHostName);

                    if (ctrlHostName.Equals(thisMachine))
                    {

                        fileName = this.runtime.getDriveLetterFileName(volumeName + shareFile);
                    }
                    else
                    {
                        fileName = ctrlFileName;
                    }

                    // Params look reasonable and FlowCreate and RapCreate match up: create the IoFlow here.

                    IoFlow f1 = runtime.CreateFlow(flowId, vmName, fileName, PreCreate, null, PreRead, PostRead, PreWrite, PostWrite, PreCleanup, null, null, null);
                    
                    //Set the remaining init parameters that are also dynamically change-able
                    cache.CacheSetFlowSize(flowId, flowCacheSize);
#if USE_GHOST_CACHE
                    cache.CacheCreateGhostCache(f1.FlowId, ghostCacheBlockSize);
#endif
                    switch (writePolicy)
                    {
                        case "write-through":
                            cache.SetCacheWritePolicy(CacheWritePolicy.WriteThrough);
                            break;
                    }
                    switch (writeCachePolicy)
                    {
                        case "CacheWrites":
                            cache.SetCacheWriteBuffering(CacheWriteBuffer.Cache);
                            break;
                        case "noCacheWrites":
                            cache.SetCacheWriteBuffering(CacheWriteBuffer.noCache);
                            break;
                    }


                    DictIoFlow.Add(flowc.FlowId, f1);

                }
            }
            return OktoResultCodes.OKTO_RESULT_SUCCESS;
        }

        /// <summary>
        /// Callback when slave receives MessageStatsZero.
        /// This is a request from Rate Controller to reset all flow stats to zero.
        /// </summary>
        /// <returns></returns>
        public OktoResultCodes CallbackMessageStatsZero()
        {
            //Console.WriteLine("CallbackMessageStatsZero()");
            return OktoResultCodes.OKTO_RESULT_SUCCESS;
        }

        /// <summary>
        /// Callback when slave receives MessageIoFlowUpdate.
        /// Typically one of these messages updates a batch of flows.
        /// This is a request from the Rate Controller to update state for the given flows.
        /// </summary>
        /// <param name="listParams"></param>
        /// <returns></returns>
        public OktoResultCodes CallbackMessageIoFlowUpdate(List<IoFlowMessageParams> listParams)
        {
            lock (LockFlows)
            {
                foreach (IoFlowMessageParams flowu in listParams)
                {
                    IoFlow flow = DictIoFlow[flowu.FlowId];

                    parseControllerCommandString(flow, flowu.ParameterString);
                }
            }
            return OktoResultCodes.OKTO_RESULT_SUCCESS;
        }

        private void parseControllerCommandString(IoFlow flow, string controllerCommand)
        {
            string[] commandStringSeparators = new string[] { " ", "\t" };
            char[] tokenSeparator = new char[] { '=' };
            string[] commandTokens = controllerCommand.Split(commandStringSeparators, StringSplitOptions.RemoveEmptyEntries);

            if (commandTokens.Length > 0)
            {
                Console.WriteLine("CallbackMessageIoFlowUpdate({0},{1})", flow.FlowId, controllerCommand);
                foreach (string token in commandTokens)
                {
                    string param = token.Split(tokenSeparator)[0];
                    string value = token.Split(tokenSeparator)[1];


                    //Switch to the appropriate handler
                    switch (param)
                    {
                        case "changeCacheSize":
                            this.cache.CacheSetFlowSize(flow.FlowId, Convert.ToUInt64(value));
                            break;
                        case "changeWritePolicy":
                            switch (value)
                            {
                                case "write-through":
                                    this.cache.SetCacheWritePolicy(CacheWritePolicy.WriteThrough);
                                    break;
                                default:
                                    Console.WriteLine("Command value invalid");
                                    Debug.Assert(0 == 1);
                                    break;
                            }
                            break;
                        case "changeOpPolicy":
                            string op = value.Split(',')[0];
                            string policy = value.Split(',')[1];
                            switch (op)
                            {
                                case "W":
                                    switch (policy)
                                    {
                                        case "yes":
                                            this.cache.SetCacheWriteBuffering(CacheWriteBuffer.Cache); //Cache writes
                                            break;
                                        case "no":
                                            this.cache.SetCacheWriteBuffering(CacheWriteBuffer.noCache); //Don't cache writes 
                                            break;
                                        default:
                                            Console.WriteLine("Command value invalid");
                                            Debug.Assert(0 == 1);
                                            break;
                                    }
                                    break;
                                case "R":
                                    //Nothing for now; add fine-grained control to cache/not cache reads here
                                    break;
                                default:
                                    Console.WriteLine("Command value invalid");
                                    Debug.Assert(0 == 1);
                                    break;
                            }
                            break;
                        default:
                            Console.WriteLine("Controller command invalid");
                            Debug.Assert(0 == 1);
                            break;
                    }
                }
            }
        }

        private string formatCacheStatsString(UInt32 flowID)
        {
            FlowSLA flowSLA = this.cache.FlowStats[flowID];

#if USE_GHOST_CACHE

            //XXXIS 2 options: can set specMaxCache to be the max size of the cache curve being collected, OR, default is to take numCurvePoints along the current allocated
            //cache size. Controller will serialize entire curve.

            //UInt64 specMaxCache = 21474836480;
            int numCurvePoints = 500;
            float stepSize = 1.0F / (float)numCurvePoints;
            List<UInt64> specSizes = new List<UInt64>();
            for (float frac = 0.0F; frac <= (1.0F+stepSize); frac += stepSize)
            {
                UInt32 ghostCacheBlockSize = flowSLA.GhostCache.cacheBlockSize;
                //Compute and block-align the cache sizes; XXXIS: THIS ASSUMES ghost cache block size is a power of two!!!

                //UInt64 specCacheSize = ((UInt64)(frac * (float)specMaxCache)) & (~((UInt64)(ghostCacheBlockSize - 1)));
                UInt64 specCacheSize = ((UInt64)(frac * (float)this.cache.FileCacheSize)) & (~((UInt64)(ghostCacheBlockSize - 1)));
                specSizes.Add(specCacheSize);
            }
            string cacheDemandCurve = flowSLA.GhostCache.CacheDemandCurve(specSizes);
#else
            string cacheDemandCurve = "";
#endif

            return String.Format("cacheSizeAllocated={0} cacheSizeUsed={1} cacheAccessesTotal={2} flowBytesAccessed={3} cacheAccessesHits={4} cacheEvictions={5} {6}",
                flowSLA.FlowCacheSize, flowSLA.FlowCacheSizeUsedBytes, flowSLA.CacheAccessesTotal, flowSLA.FlowBytesAccessed , flowSLA.CacheAccessesHits, 
                flowSLA.CacheTotalEvictions, cacheDemandCurve);
        }

        /// <summary>
        /// Callback when slave receives MessageIoFlowStatsQuery.
        /// The reply message generated herein provides stats for all flows of the current tenant.
        /// This is a request from Rate Controller to return stats for all flows for current tenant.
        /// </summary>
        /// <returns></returns>
        public List<IoFlowMessageParams> CallbackMessageIoFlowStatsQuery()
        {
            //Console.WriteLine("CallbackMessageIoFlowStatsQuery()");
            List<IoFlowMessageParams> listStats = new List<IoFlowMessageParams>();
            lock (LockFlows)
            {
                foreach (IoFlowMessageParams statsquery in DictFlowCreateParams.Values)
                {
                    IoFlow flow = DictIoFlow[statsquery.FlowId];
                    listStats.Add(new IoFlowMessageParams(flow.FlowId, formatCacheStatsString(flow.FlowId), (byte)flow.Flags));
                }
            }
            return listStats;
        }

        /// <summary>
        /// Callback when slave receives MessageTenantDelete.
        /// Typically clear down local state and driver state. 
        /// This is a request from Rate Controller to delete all state for this tenant.
        /// In practice though we tend to clear down on socket error when Rate Controller rudely shuts down.
        /// </summary>
        /// <returns></returns>
        public OktoResultCodes CallbackMessageTenantDelete()
        {
            Console.WriteLine("MessageTenantDelete()");
            //Reset();
            return OktoResultCodes.OKTO_RESULT_SUCCESS;
        }

        /// <summary>
        /// Callback when socket reports close.
        /// Typically clear down local state and driver state. 
        /// </summary>
        /// <param name="conn"></param>
        public void CallbackReceiveClose(Connection conn)
        {
            Console.WriteLine("MoiraiSlave CallbackReceiveClose: received close (zero bytes) from conn {0}", conn.HostName);
            Reset();
        }

        /// <summary>
        /// Callback when socket reports and error.
        /// Typically clear down local state and driver state. 
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="errNo"></param>
        public void CallbackReceiveError(Connection conn, int errNo)
        {
            string msg = String.Format("MoiraiSlave CallbackReceiveError TCP comms error: conn = {0}, err = {1}", conn.HostName, errNo);
            Reset();
        }

        /// <summary>
        /// Callback when socket exception has been caught.
        /// </summary>
        /// <param name="sockEx"></param>
        public void CallbackCatchSocketException(SocketException sockEx)
        {
            Console.WriteLine("MoiraiSlave CallbackCatchSocketException({0})", sockEx.Message);
            Reset();
        }

        #endregion

        #region callbacks IoFlow
        private PreCreateReturnCode PreCreate(IRP irp)
        {
            return this.cache.CachePreCreate(irp);
        }
        private PreReadReturnCode PreRead(IRP irp)
        {
            return this.cache.CacheIRP(irp);
        }
        private PostReadReturnCode PostRead(IRP irp)
        {
            return cache.CacheIRPCompleted(irp);
        }
        private PreWriteReturnCode PreWrite(IRP irp)
        {
            return cache.CacheWriteIRP(irp);
        }
        private PostWriteReturnCode PostWrite(IRP irp)
        {
            return cache.CacheWriteIRPCompleted(irp);
        }
        private PreCleanupReturnCode PreCleanup(IRP irp)
        {
            return cache.CachePreCleanup(irp);
        }

        #endregion

    }
}