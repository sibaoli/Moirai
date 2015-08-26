//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************
//#define LINEAR_STACK
#define SORTED_LIST

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ExampleCacheNamespace
{
    public class ReverseComparer : IComparer<UInt64>
    {
        public int Compare(UInt64 x, UInt64 y)
        {
            return y.CompareTo(x);
        }
    }

    public class MattsonGhostCache
    {
        private class GhostCacheElement
        {
            public UInt64 refreshCounter;
            public string cacheBlock;

            public GhostCacheElement(UInt64 counter, string block)
            {
                this.refreshCounter = counter;
                this.cacheBlock = block;
            }
        }

        bool noisyOutput;

#if SORTED_LIST
        private SortedList<UInt64, GhostCacheElement> stack;
        private Dictionary<string, GhostCacheElement> reverseLookup;
        private UInt64 refreshCounter;
#endif

#if LINEAR_STACK
        public List<string> stack; //XXXIS for now, keys are strings that are concatenations of the form <file,blockID> , general implementation later...
#endif

        public UInt32 cacheBlockSize; //The size of a block in this cache in bytes
        //Int32 totalAvailCacheBlocks; //Total number of cache blocks available
        Int64 totalAvailCacheBlocks; //Total number of cache blocks available

        UInt64 totalCacheAccesses;
        UInt32[] depthsAccesses; //Stack depth counters; Uint32 for now
        UInt64 coldMisses; //Counter for number of cold misses, in case we are ever interested

        UInt64 maxCacheSizeBytes; //Max. size of the cache in bytes
        
        public MattsonGhostCache(UInt32 cacheBlockSize, UInt64 maxCacheSizeBytes, bool noisyOutput)
        {
            Debug.Assert(maxCacheSizeBytes % cacheBlockSize == 0);
            
            UInt64 num_blocks_req = maxCacheSizeBytes / cacheBlockSize;
            //if (num_blocks_req > Int32.MaxValue)
            //{
            //    Console.WriteLine("StackDepthGhostCache FAILED to initialize; {0} blocks of size {1} == {2} bytes total is too large",
            //        num_blocks_req, cacheBlockSize, maxCacheSizeBytes);
            //    Debug.Assert(0 == 1);
            //}

            //this.totalAvailCacheBlocks = (Int32)num_blocks_req;
            this.totalAvailCacheBlocks = (Int64)num_blocks_req;

#if LINEAR_STACK
            this.stack = new List<string>();
#endif

#if SORTED_LIST
            this.stack = new SortedList<UInt64, GhostCacheElement>(new ReverseComparer());
            reverseLookup = new Dictionary<string, GhostCacheElement>();
            this.refreshCounter = 0;
#endif
            this.cacheBlockSize = cacheBlockSize;
            this.maxCacheSizeBytes = maxCacheSizeBytes;
            this.totalCacheAccesses = 0;
            this.depthsAccesses = new UInt32[num_blocks_req];

            //Initialize counters to 0
            for (Int64 i = 0; i < this.totalAvailCacheBlocks; i++)
            {
                this.depthsAccesses[i] = 0;
            }
            //Array.Clear(this.depthsAccesses, 0, (int)num_blocks_req);

            this.noisyOutput = noisyOutput;
        }

        /// <summary>
        /// Performs and eviction of neccessary (this should happen very rarely in the ghost cache...)
        /// </summary>
        private void Evict()
        {
            Debug.Assert(this.stack.Count <= this.totalAvailCacheBlocks);

            while (this.stack.Count >= totalAvailCacheBlocks && this.stack.Count > 0)
            {
#if SORTED_LIST
                UInt64 min_elem_key = this.stack.Keys.Min();
                string evictBlockName = this.stack[min_elem_key].cacheBlock;
                if (noisyOutput)
                    Console.WriteLine("StackDepthGhostCache Evicting block {0} with counter {1}", evictBlockName, min_elem_key);
                this.stack.Remove(min_elem_key);
                this.reverseLookup.Remove(evictBlockName);
#endif


            }
        }

#if LINEAR_STACK
        /// <summary>
        /// Callled on a reference (read/write) to a cache block
        /// </summary>
        /// <param name="cacheBlock">should be a string concatenation of "fileName" and "blockID"</param>
        public void CacheReadReference(string cacheBlock){
            //Stopwatch timer = new Stopwatch();
            //timer.Start();

            int blockIndex=-1;

            this.totalCacheAccesses++; //Increment the total number of references we've seen in the cache so far

            if ((blockIndex = this.stack.IndexOf(cacheBlock)) >= 0)
            { //hit, it's in the cache
                //update the counter at its current depth
                Debug.Assert(this.depthsAccesses[blockIndex] + 1 < UInt32.MaxValue); //make sure we don't overflow the counter
                this.depthsAccesses[blockIndex] += 1;

                //Move it to the top of the stack (0-based top)
                lock (this.stack)
                {
                    this.stack.RemoveAt(blockIndex);
                    this.stack.Insert(0, cacheBlock);
                }
            }
            else //miss, it's not in the cache
            {
                this.Evict(); //check if we need to free up space (should rarely have to...)

                lock (this.stack)
                {
                    this.stack.Insert(0, cacheBlock);
                    this.coldMisses++; //this is only true (ie: a cold miss) until the ghost cache has to start evicting things
                }
            }

            //Console.WriteLine("time {0}", timer.Elapsed);
        }
#endif

#if SORTED_LIST
        public void CacheReadReference(string cacheBlock)
        {
            int blockIndex = -1;

            this.totalCacheAccesses++; //Increment the total number of references we've seen in the cache so far
            this.refreshCounter++; //increment the refresh counter

            GhostCacheElement ghostBlock = null;

            if (this.reverseLookup.TryGetValue(cacheBlock, out ghostBlock)) //O(1)
            { //hit, it's in the cache
                blockIndex = this.stack.IndexOfKey(ghostBlock.refreshCounter); // O(log n)
                ghostBlock.refreshCounter = this.refreshCounter; //update counter in the block
                //update the counter at its current depth
                Debug.Assert(this.depthsAccesses[blockIndex] + 1 < UInt32.MaxValue); //make sure we don't overflow the counter
                this.depthsAccesses[blockIndex] += 1;
                //Move it to the right place in the stack (new counter value == "0th position")

                this.stack.RemoveAt(blockIndex); //O(log n)
                this.stack.Add(this.refreshCounter, ghostBlock); //O(log n)
            }
            else
            {
                this.Evict(); //check if we need to free up space (should rarely have to...)
                ghostBlock = new GhostCacheElement(refreshCounter, cacheBlock);

                this.stack.Add(refreshCounter, ghostBlock);
                this.reverseLookup.Add(cacheBlock, ghostBlock);
                this.coldMisses++; //this is only true (ie: a cold miss) until the ghost cache has to start evicting things
            }
        }
#endif

        ///// <summary>
        ///// Calculates the "What If?" hit rate for a given cache size
        ///// </summary>
        ///// <param name="speculativeCacheSize"></param>
        ///// <returns></returns>
        //public float CacheHitRate(UInt64 speculativeCacheSize)
        //{
        //    Debug.Assert(speculativeCacheSize % this.cacheBlockSize == 0);
        //    float hitRate = 0.0F;

        //    UInt64 numBlocksSpec = speculativeCacheSize / this.cacheBlockSize;
        //    if (numBlocksSpec > Int32.MaxValue )
        //    {
        //        Console.WriteLine("StackDepthGhostCache cannot answer query; {0} bytes cache size is too large", speculativeCacheSize);
        //        Debug.Assert(0 == 1);
        //    }

        //    UInt64 speculativeHits = 0;

        //    for (int i = 0; i < (Int32)numBlocksSpec; i++)
        //    {
        //        speculativeHits += this.depthsAccesses[i];
        //    }

        //    //Console.WriteLine("\t GHOST h: {0} t: {1}", speculativeHits, this.totalCacheAccesses);

        //    hitRate = (this.totalCacheAccesses > 0 ? (float)speculativeHits / (float)this.totalCacheAccesses : 0.0F);

        //    return hitRate;
        //}

        public float CacheHitRate(UInt64 partialSumSize, ref UInt64 partialSum, UInt64 speculativeCacheSize)
        {
            Debug.Assert(speculativeCacheSize % this.cacheBlockSize == 0);
            Debug.Assert(partialSumSize % this.cacheBlockSize == 0);
            float hitRate = 0.0F;

            UInt64 numBlocksSpec = speculativeCacheSize / this.cacheBlockSize;
            UInt64 partialSumIndex = partialSumSize / this.cacheBlockSize;
            if (numBlocksSpec > Int32.MaxValue)
            {
                Console.WriteLine("StackDepthGhostCache cannot answer query; {0} bytes cache size is too large", speculativeCacheSize);
                Debug.Assert(0 == 1);
            }

            UInt64 speculativeHits = partialSum;

            for (UInt64 i = partialSumIndex; i < numBlocksSpec; i++)
            {
                speculativeHits += this.depthsAccesses[i];
            }

            //Console.WriteLine("\t GHOST h: {0} t: {1}", speculativeHits, this.totalCacheAccesses);

            hitRate = (this.totalCacheAccesses > 0 ? (float)speculativeHits / (float)this.totalCacheAccesses : 0.0F);

            partialSum = speculativeHits;
            return hitRate;
        }


        public string CacheDemandCurve(List<UInt64> specCacheSizes){
            string returnCurvePoints = "";

            UInt64 specSize;

            UInt64 partialSum = 0;

            for (int i = 0; i < specCacheSizes.Count; i++)
            {
                specSize = specCacheSizes[i];
                Debug.Assert(specSize % this.cacheBlockSize == 0);

                float specHitRate;

                if (i == 0)
                {
                    specHitRate = this.CacheHitRate(0, ref partialSum,specSize);
                }
                else
                {
                    specHitRate = this.CacheHitRate(specCacheSizes[i-1], ref partialSum, specSize);
                }
                

                returnCurvePoints += specSize.ToString() + "," + specHitRate.ToString();

                if (i != (specCacheSizes.Count - 1))
                {
                    returnCurvePoints += ";";
                }
            }
            return returnCurvePoints;
        }
    }
}
