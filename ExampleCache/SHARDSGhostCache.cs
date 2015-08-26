using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;


namespace ExampleCacheNamespace
{
    
    public static class IntHelpers
    {
        public static ulong RotateLeft(this ulong original, int bits)
        {
            return (original << bits) | (original >> (64 - bits));
        }

        public static ulong RotateRight(this ulong original, int bits)
        {
            return (original >> bits) | (original << (64 - bits));
        }

        unsafe public static ulong GetUInt64(this byte[] bb, int pos)
        {
            // we only read aligned longs, so a simple casting is enough
            fixed (byte* pbyte = &bb[pos])
            {
                return *((ulong*)pbyte);
            }
        }
    }


    public class SHARDSGhostCache
    {

        /// <summary>
        /// http://blog.teamleadnet.com/2012/08/murmurhash3-ultra-fast-hash-algorithm.html
        /// </summary>
        class Murmur3
        {
            // 128 bit output, 64 bit platform version
            public static ulong READ_SIZE = 16;
            private static ulong C1 = 0x87c37b91114253d5L;
            private static ulong C2 = 0x4cf5ad432745937fL;

            private uint seed; // if want to start with a seed, create a constructor

            public Murmur3()
            {
                Random rnd = new Random();
                seed = (uint)rnd.Next();
            }

            public byte[] ComputeHash(byte[] bb)
            {
                ulong h1 = 0L;
                ulong h2 = 0L;
                
                h1 = seed;
                ulong length = 0L;

                int pos = 0;
                ulong remaining = (ulong)bb.Length;

                // read 128 bits, 16 bytes, 2 longs in eacy cycle
                while (remaining >= READ_SIZE)
                {
                    ulong k1 = IntHelpers.GetUInt64(bb, pos);
                    pos += 8;

                    ulong k2 = IntHelpers.GetUInt64(bb, pos);
                    pos += 8;

                    length += READ_SIZE;
                    remaining -= READ_SIZE;

                    //Used to be MixBody(k1, k2);
                    k1 *= C1;
                    k1 = IntHelpers.RotateLeft(k1, 31);
                    k1 *= C2;
                    h1 ^= k1; //was h1 ^= MixKey1(k1);

                    h1 = IntHelpers.RotateLeft(h1, 27);
                    h1 += h2;
                    h1 = h1 * 5 + 0x52dce729;

                    k2 *= C2;
                    k2 = IntHelpers.RotateLeft(k2, 33);
                    k2 *= C1;
                    h2 ^= k2; //was h2 ^= MixKey2(k2);

                    h2 = IntHelpers.RotateLeft(h2, 31);
                    h2 += h1;
                    h2 = h2 * 5 + 0x38495ab5;
                }

                // if the input MOD 16 != 0
                if (remaining > 0)
                {
                    //ProcessBytesRemaining(bb, remaining, pos);
                    ulong j1 = 0;
                    ulong j2 = 0;
                    length += remaining;

                    // little endian (x86) processing
                    switch (remaining)
                    {
                        case 15:
                            j2 ^= (ulong)bb[pos + 14] << 48; // fall through
                            goto case 14;
                        case 14:
                            j2 ^= (ulong)bb[pos + 13] << 40; // fall through
                            goto case 13;
                        case 13:
                            j2 ^= (ulong)bb[pos + 12] << 32; // fall through
                            goto case 12;
                        case 12:
                            j2 ^= (ulong)bb[pos + 11] << 24; // fall through
                            goto case 11;
                        case 11:
                            j2 ^= (ulong)bb[pos + 10] << 16; // fall through
                            goto case 10;
                        case 10:
                            j2 ^= (ulong)bb[pos + 9] << 8; // fall through
                            goto case 9;
                        case 9:
                            j2 ^= (ulong)bb[pos + 8]; // fall through
                            goto case 8;
                        case 8:
                            j1 ^= bb.GetUInt64(pos);
                            break;
                        case 7:
                            j1 ^= (ulong)bb[pos + 6] << 48; // fall through
                            goto case 6;
                        case 6:
                            j1 ^= (ulong)bb[pos + 5] << 40; // fall through
                            goto case 5;
                        case 5:
                            j1 ^= (ulong)bb[pos + 4] << 32; // fall through
                            goto case 4;
                        case 4:
                            j1 ^= (ulong)bb[pos + 3] << 24; // fall through
                            goto case 3;
                        case 3:
                            j1 ^= (ulong)bb[pos + 2] << 16; // fall through
                            goto case 2;
                        case 2:
                            j1 ^= (ulong)bb[pos + 1] << 8; // fall through
                            goto case 1;
                        case 1:
                            j1 ^= (ulong)bb[pos]; // fall through
                            break;
                        default:
                            throw new Exception("Something went wrong with remaining bytes calculation.");
                    }

                    j1 *= C1;
                    j1 = IntHelpers.RotateLeft(j1, 31);
                    j1 *= C2;
                    h1 ^= j1;

                    j2 *= C2;
                    j2 = IntHelpers.RotateLeft(j2, 33);
                    j2 *= C1;
                    h2 ^= j2;
                }




                /////////////////////////////////////
                //// BELOW WAS ALL return Hash;
                /////////////////////////////////////

                h1 ^= length;
                h2 ^= length;

                h1 += h2;
                h2 += h1;

                //h1 = Murmur3.MixFinal(h1);
                h1 ^= h1 >> 33;
                h1 *= 0xff51afd7ed558ccdL;
                h1 ^= h1 >> 33;
                h1 *= 0xc4ceb9fe1a85ec53L;
                h1 ^= h1 >> 33;
                
                //h2 = Murmur3.MixFinal(h2);
                h2 ^= h2 >> 33;
                h2 *= 0xff51afd7ed558ccdL;
                h2 ^= h2 >> 33;
                h2 *= 0xc4ceb9fe1a85ec53L;
                h2 ^= h2 >> 33;
                
                h1 += h2;
                h2 += h1;

                var hash = new byte[Murmur3.READ_SIZE];

                Array.Copy(BitConverter.GetBytes(h1), 0, hash, 0, 8);
                Array.Copy(BitConverter.GetBytes(h2), 0, hash, 8, 8);

                return hash;
            }



           
        }


        private class GhostCacheElement
        {
            public UInt64 refreshCounter;
            public UInt64 cacheBlockID;

            public GhostCacheElement(UInt64 counter, UInt64 block)
            {
                this.refreshCounter = counter;
                this.cacheBlockID = block;
            }
        }

        bool noisyOutput;

        private Murmur3 Murmur3HashFunction; 
        private SortedList<UInt64, GhostCacheElement> distanceTree;
        private Dictionary<UInt64, GhostCacheElement> hashTableLookup;
        private UInt32[] reuseAccessHistogram; //Stack depth counters; Uint32 for now

        private UInt64 refreshCounter;

        private UInt64 modulusP;
        private Int32 modulusPPower;
        private UInt64 thresholdT; 
        private Double samplingRateR;


        public UInt32 cacheBlockSize; //The size of a block in this cache in bytes
        //Int32 totalAvailCacheBlocks; //Total number of cache blocks available
        Int64 totalAvailCacheBlocks; //Total number of cache blocks available

        UInt64 totalCacheReferences; //Total references in the wokload
        UInt64 totalProcessedReferences; //Total processed (met the hashing criteria) references
        UInt64 coldMisses; //Counter for number of cold misses, in case we are ever interested

        UInt64 maxCacheSizeBytes; //Max. size of the cache in bytes


        public SHARDSGhostCache(UInt32 cacheBlockSize, UInt64 maxCacheSizeBytes, bool noisyOutput)
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

            //set the sampling parameters 
            this.modulusP = 16777216; //2^24, as specified in the paper
            this.modulusPPower = 24; // power of the modulus (for efficient reuse distance scaling)
            this.thresholdT = 167772; // yields sampling rate of 0.01
            this.samplingRateR = (double)this.thresholdT / (double)this.modulusP; //just in case we need the sampling rate in a local variable
            this.Murmur3HashFunction = new Murmur3();

            this.distanceTree = new SortedList<UInt64, GhostCacheElement>(new ReverseComparer());
            this.hashTableLookup = new Dictionary<UInt64, GhostCacheElement>();
            this.refreshCounter = 0;

            this.cacheBlockSize = cacheBlockSize;
            this.maxCacheSizeBytes = maxCacheSizeBytes;
            this.totalCacheReferences = 0;
            this.totalProcessedReferences = 0;
            this.reuseAccessHistogram = new UInt32[(Int64)num_blocks_req];

            //Initialize counters to 0
            for (Int64 i = 0; i < this.totalAvailCacheBlocks; i++)
            {
                this.reuseAccessHistogram[i] = 0;
            }
            //Array.Clear(this.reuseAccessHistogram, 0, (Int32)num_blocks_req);

            this.noisyOutput = noisyOutput;
        }

        /// <summary>
        /// Performs and eviction of neccessary (this should happen very rarely in the ghost cache...)
        /// </summary>
        private void Evict()
        {
            Debug.Assert(this.distanceTree.Count <= this.totalAvailCacheBlocks);

            while (this.distanceTree.Count >= totalAvailCacheBlocks && this.distanceTree.Count > 0)
            {
                UInt64 min_elem_key = this.distanceTree.Keys.Min();
                UInt64 evictBlockName = this.distanceTree[min_elem_key].cacheBlockID;
                if (noisyOutput)
                    Console.WriteLine("StackDepthGhostCache Evicting block {0} with counter {1}", evictBlockName, min_elem_key);
                this.distanceTree.Remove(min_elem_key);
                this.hashTableLookup.Remove(evictBlockName);
            }
        }




        public void CacheReadReference(string cacheBlock)
        {
            this.refreshCounter++; //increment the refresh counter
            this.totalCacheReferences++; //Increment the total number of references we've seen in the cache so far

            Int32 blockIndex = -1;
            byte[] hashedByteValue = this.Murmur3HashFunction.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cacheBlock));
            UInt64 hashedValue = IntHelpers.GetUInt64(hashedByteValue, 8);

            if ((hashedValue & (this.modulusP - 1)) < this.thresholdT){
                this.totalProcessedReferences++; 
                
                GhostCacheElement ghostBlock = null;

                if (this.hashTableLookup.TryGetValue(hashedValue, out ghostBlock)) //O(1)
                { //hit, it's in the ghost cache
                    blockIndex = this.distanceTree.IndexOfKey(ghostBlock.refreshCounter); // O(log n)
                    
                    Int32 scaledReuseDistance = (Int32)Math.Round((double)blockIndex / this.samplingRateR);

                    ghostBlock.refreshCounter = this.refreshCounter; //update counter in the block
                    //update the counter at its current depth
                    Debug.Assert(this.reuseAccessHistogram[scaledReuseDistance] + 1 < UInt32.MaxValue); //make sure we don't overflow the counter
                    this.reuseAccessHistogram[scaledReuseDistance] += 1;
                    //Move it to the right place in the stack (new counter value == "0th position")
                    this.distanceTree.RemoveAt(blockIndex); //O(log n)
                    this.distanceTree.Add(this.refreshCounter, ghostBlock); //O(log n)
                }
                else
                {
                    this.Evict(); //check if we need to free up space (should rarely have to...)
                    ghostBlock = new GhostCacheElement(refreshCounter, hashedValue);

                    this.distanceTree.Add(refreshCounter, ghostBlock);
                    this.hashTableLookup.Add(hashedValue, ghostBlock);
                    this.coldMisses++; //this is only true (ie: a cold miss) until the ghost cache has to start evicting things
                }
            }
            
        }


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
        //        speculativeHits += this.reuseAccessHistogram[i];
        //    }

        //    //Console.WriteLine("\t GHOST h: {0} t: {1}", speculativeHits, this.totalCacheAccesses);

        //    hitRate = (this.totalCacheReferences > 0 ? (float)speculativeHits / (float)this.totalCacheReferences : 0.0F);

        //    return hitRate / (float)this.samplingRateR; //remember to divide by the sampling rate to account for the sampling
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
                Console.WriteLine("SHARDSGhostCache cannot answer query; {0} bytes cache size is too large", speculativeCacheSize);
                Debug.Assert(0 == 1);
            }

            UInt64 speculativeHits = partialSum;

            for (UInt64 i = partialSumIndex; i < numBlocksSpec; i++)
            {
                speculativeHits += this.reuseAccessHistogram[i];
            }

            //Console.WriteLine("\t GHOST h: {0} t: {1}", speculativeHits, this.totalCacheAccesses);

            hitRate = (this.totalCacheReferences > 0 ? (float)speculativeHits / (float)this.totalCacheReferences : 0.0F);
            
            partialSum = speculativeHits;

            return hitRate / (float)this.samplingRateR; //remember to divide by the sampling rate to account for the sampling
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
                    specHitRate = this.CacheHitRate(0, ref partialSum, specSize);
                }
                else
                {
                    specHitRate = this.CacheHitRate(specCacheSizes[i - 1], ref partialSum, specSize);
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
