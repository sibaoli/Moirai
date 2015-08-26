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
using System.Diagnostics;

namespace ExampleCacheNamespace
{
    // maintains stats on a flow
    public class FlowSLA
    {
        private UInt32 flowID;
        public UInt32 FlowID { get { return flowID; } set { flowID = value; } }

        // cache size in Bytes per flow
        private UInt64 flowCacheSize;
        public UInt64 FlowCacheSize {get {return flowCacheSize;} set {flowCacheSize = value;}}

        private UInt64 flowCacheSizeUsedBytes;
        public UInt64 FlowCacheSizeUsedBytes { get { return flowCacheSizeUsedBytes; } set { flowCacheSizeUsedBytes = value; } }

        private UInt64 cacheAccessesTotalHits;
        public UInt64 CacheAccessesHits { get { return cacheAccessesTotalHits; } set { Debug.Assert(cacheAccessesTotalHits + value <= UInt64.MaxValue); 
            cacheAccessesTotalHits = value; } }

        private UInt64 cacheAccessesTotal;
        public UInt64 CacheAccessesTotal { get { return cacheAccessesTotal; } set { Debug.Assert(cacheAccessesTotal + value <= UInt64.MaxValue); 
            cacheAccessesTotal = value; } }

        private UInt64 flowBytesAccessed;
        public UInt64 FlowBytesAccessed { get { return flowBytesAccessed; } set { Debug.Assert(flowBytesAccessed + value <= UInt64.MaxValue);
                flowBytesAccessed = value; } }

        private UInt64 cacheTotalEvictions;
        public UInt64 CacheTotalEvictions { get { return cacheTotalEvictions; } set { Debug.Assert(cacheTotalEvictions + value <= UInt64.MaxValue);
            cacheTotalEvictions = value; } }

        private MattsonGhostCache ghostCache;
        public MattsonGhostCache GhostCache { get { return ghostCache; } }

        private UInt32 ioRequestSize;
        public UInt32 IORequestSize { get { return ioRequestSize;} set { ioRequestSize = value;} }

        // list of cached entries
        public LinkedList<FileCacheElement> cacheEntries;

        // constructor
        public FlowSLA(UInt32 flowID)
        {
            flowCacheSize = 0;
            flowCacheSizeUsedBytes = 0;
            cacheEntries = new LinkedList<FileCacheElement>();
            cacheAccessesTotalHits = 0;
            cacheAccessesTotal = 0;
            flowBytesAccessed = 0;
            cacheTotalEvictions = 0;
            this.flowID = flowID;
            this.ghostCache = null;
        }

        public bool createGhostCache(UInt32 ioRequestSize, UInt64 maxCacheSizeBytes, bool noisyOutput)
        {
            if (this.ghostCache != null) // If a ghost cache already exists, don't make a new one
            {
                return false;
            }
            else
            {
                this.ioRequestSize = ioRequestSize;
                this.ghostCache = new MattsonGhostCache(ioRequestSize, maxCacheSizeBytes, noisyOutput);

                return true;
            }

        }

        public bool destroyGhostCache()
        {
            if (this.ghostCache != null)
            {
                this.ghostCache = null;
                return true;
            }
            return false;
        }

        public bool FlowSLAHasGhostCache()
        {
            return this.ghostCache != null;
        }
        
    }
}
