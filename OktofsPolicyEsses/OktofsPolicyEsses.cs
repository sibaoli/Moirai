#define CALCULATE_ALLOCATION

//#define MAX_MIN_ALLOCATION
//#define UTILITY_MAXIMIZATION
#define END_TO_END_BW

//#define COLLECT_CACHE_CURVE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OktofsRateControllerNamespace;
using System.Threading;
using System.Diagnostics;
using System.IO;
using ExampleCacheNamespace;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml.Serialization;

namespace OktofsPolicyEssesNamespace
{
    public class OktofsPolicyEsses : OktofsPolicy
    {
        [Serializable()]
        private class CacheCurvePoints
        {
            public Double[] xVals;
            public Double[] yVals;
            public CacheCurvePoints(Double[] xVals, Double[] yVals)
            {
                this.xVals = xVals;
                this.yVals = yVals;
            }
        }

        private class EssesFlowCacheContext
        {
            public Flow Flow;
            public string tenantID;

            public CacheWritePolicy writePolicy;
            public CacheWriteBuffer cacheWrites;
            public UInt64 cacheSizeAllocated;
            public float hitRate;
            public UInt64 cacheSizeUsed;
            public UInt64 cacheAccessesTotal;
            public UInt64 flowBytesAccessedTotal;
            public UInt64 cacheAccessesHitsTotal;
            public UInt64 cacheEvictionsTotal;

            public UInt64 cacheAccessesLastAllocInterval;
            public UInt64 flowBytesAccessedLasAllocInterval;
            public UInt64 cacheAccessesHitsLastAllocInterval;
            public UInt64 cacheEvictionsLastAllocInterval;

            public UInt64 flowBytesAccessedLastSampleInterval;

            //public UInt64 flowDemand;
            //public UInt64 minFlowGuarantee;

            public UInt64 guaranteedE2EBW;

            public CacheCurvePoints cacheDemandCurvePoints;
            public string demandPointsSerializationFile;
            public IInterpolation cacheDemandFunc;

            public bool useSerializedCurve;


            //XXXIS add more things if we need them?

            public EssesFlowCacheContext(Flow flow, UInt64 cacheSizeAllocated, CacheWritePolicy writePolicy, CacheWriteBuffer cacheWrites, string cacheCurvePointsFile, string tenantID)
            {
                Flow = flow;
                this.cacheSizeAllocated = cacheSizeAllocated;
                this.writePolicy = writePolicy;
                this.cacheWrites = cacheWrites;
                this.hitRate = 0.0F;
                this.tenantID = tenantID;

                this.demandPointsSerializationFile = cacheCurvePointsFile;
                
                if (File.Exists(this.demandPointsSerializationFile))
                {
                    readInCacheCurve();
                    this.useSerializedCurve = true;
                }
                else
                {
                    this.cacheDemandCurvePoints = null;
                    this.cacheDemandFunc = null;
                    this.useSerializedCurve = false;
                }

#if COLLECT_CACHE_CURVE
                Console.CancelKeyPress += new ConsoleCancelEventHandler(Controller_CancelKeyPress);
#endif
            }

#if COLLECT_CACHE_CURVE
            void Controller_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
            {
                writeOutCacheCurve();
            }

            private void writeOutCacheCurve()
            {
                StreamWriter fileOut = new StreamWriter(this.demandPointsSerializationFile);
                for (int i = 0; i < this.cacheDemandCurvePoints.xVals.Length; i++)
                {
                    fileOut.WriteLine("{0},{1}", this.cacheDemandCurvePoints.xVals[i], this.cacheDemandCurvePoints.yVals[i]);
                }
                fileOut.Close();
            }
#endif

            private void readInCacheCurve()
            {
                string[] lines = System.IO.File.ReadAllLines(this.demandPointsSerializationFile);
                Double[] xVals = new Double[lines.Length];
                Double[] yVals = new Double[lines.Length];

                int i = 0;
                foreach (string inputRecord in lines)
                {
                    string[] inputTokens = inputRecord.Split(',');
                    xVals[i] = Convert.ToDouble(inputTokens[0]);
                    yVals[i] = Convert.ToDouble(inputTokens[1]);
                    i++;
                }
                this.cacheDemandCurvePoints = new CacheCurvePoints(xVals, yVals);
                //this.cacheDemandFunc = Interpolate.LinearBetweenPoints(this.cacheDemandCurvePoints.xVals, this.cacheDemandCurvePoints.yVals);
                //this.cacheDemandFunc = Interpolate.Linear(this.cacheDemandCurvePoints.xVals, this.cacheDemandCurvePoints.yVals);

                this.cacheDemandFunc = Interpolate.PolynomialEquidistant(this.cacheDemandCurvePoints.xVals, this.cacheDemandCurvePoints.yVals);

            }
        }

        private class EssesFileCacheContext
        {
            public UInt64 fileCacheSize;
            public List<Flow> flows; //list of flows that are active in this file cache
            //XXXIS add more things if we need them?

            public EssesFileCacheContext(UInt64 fileCacheSize)
            {
                this.fileCacheSize = fileCacheSize;
                this.flows = new List<Flow>();
            }
        }

        private UInt64 localCacheBW = 0;
        private UInt64 remoteStorageBW = 0;
        private UInt64 remoteCacheBW = 0;
        private string BWWeightsConfigFile = @"C:\Moirai_Code\IOFlowController\bw-config-ioan.txt";

        // Stats measurement parameters
        private uint settleMillisecs = 0;
        private uint sampleMillisecs = 0;

        private uint cacheAllocControlFreq = 15; //Frequency of re-allocating cache between the flows; in multiples of the stats collection interval
        private uint cacheAllocControlCounter = 0;

        private double statsDelta = 0.0;                        // get raw unfiltered stats from slaves

        // Tenant and flow state
        private List<Flow> listFlows = null;                    // list of flows from a VM to a storage volume (for now)
        private Dictionary<string, EssesFileCacheContext> fileCaches = null;

        #region ctor
        public OktofsPolicyEsses(string configFileName, string topConfigName, int slavePort)
        {
            shutdownEvent = new ManualResetEvent(false);
            //
            // initialize rate controller
            // parsing the config file defines VM placement and VM ordering within a traffic matrix
            //
            Random random = new Random();
            uint TenantId = (uint)random.Next(1, int.MaxValue); // min P of collision at slaves.

            rateController = new OktofsRateController(this, TenantId, slavePort);

            InitBandwidthWeights(BWWeightsConfigFile); //initialize the bandwidth weights from the appropriate config file

            string[] validInputRecords = new string[] { "D-VM-FILE-VOL", "CD-VM-FILE-VOL", "CH-VM-FILE-VOL", "C-VM-SHARE-VOL", "H-VM-SHARE-VOL", "CH-VM-SHARE-VOL" };
            listFlows = rateController.InitListFlows(configFileName, validInputRecords);

            fileCaches = new Dictionary<string, EssesFileCacheContext>();

            //Initialize some controller-side context about each flow's cache and the file caches they belong to based on the input tokens stored when flows were created
            foreach (Flow flow in listFlows)
            {
                UInt64 fileCacheSize = Convert.ToUInt64(flow.InputTokens[9]);
                string flowTenantID = flow.InputTokens[12];

                if (!this.fileCaches.ContainsKey(flowTenantID))
                {
                    this.fileCaches.Add(flowTenantID, new EssesFileCacheContext(fileCacheSize));
                }


                this.fileCaches[flowTenantID].flows.Add(flow); //Add this flow to the context for this file cache

                UInt64 flowCacheSizeAllocated = Convert.ToUInt64(flow.InputTokens[5]);
                CacheWritePolicy writePolicy = 0;
                CacheWriteBuffer cacheWrites = 0;
                switch (flow.InputTokens[7])
                {
                    case "write-through":
                        writePolicy = CacheWritePolicy.WriteThrough;
                        break;
                }
                switch (flow.InputTokens[8])
                {
                    case "noCacheWrites":
                        cacheWrites = CacheWriteBuffer.noCache;
                        break;
                    case "CacheWrites":
                        cacheWrites = CacheWriteBuffer.Cache;
                        break;
                }
                EssesFlowCacheContext flowContext = new EssesFlowCacheContext(flow, flowCacheSizeAllocated, writePolicy, cacheWrites, flow.InputTokens[11], flowTenantID);
                flow.Context = flowContext;

                //flowContext.minFlowGuarantee = Convert.ToUInt64(flow.InputTokens[10]);

                flowContext.guaranteedE2EBW = Convert.ToUInt64(flow.InputTokens[13]);

            }

            //
            // ask Rate Controller to create the queues on the remote servers. 
            //
            rateController.InstallFlows();

            //
            // At this point the Rate Controller is connected to the rate slaves and the
            // minifilter drivers are configured with queues. The minifilters will not enforce
            // rate limits until we install the RAPs, which is done in the Start() routine. 
            //
        }

        #endregion

        #region init

        public void InitBandwidthWeights(string configFileName)
        {
            StreamReader streamIn = new StreamReader(configFileName);
            string inputRecord;
            string[] separators = new string[] { " ", "\t" };

            while ((inputRecord = streamIn.ReadLine()) != null)
            {
                string[] inputTokens = inputRecord.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (inputTokens.Length == 0){
                    continue;
                }
                else if (inputTokens[0].StartsWith(@"#") || inputTokens[0].StartsWith(@"//")){
                    continue;
                } else if (inputTokens.Length >=3 ) {            
                //
                // Parse records.
                //

                localCacheBW = UInt64.Parse(inputTokens[0].ToLower()); // bytes/second
                remoteStorageBW = UInt64.Parse(inputTokens[1].ToLower()); // bytes/second
                remoteCacheBW = UInt64.Parse(inputTokens[2].ToLower()); // bytes/second

                } else {
                    string msg = String.Format("Illegal config file line: {0}", inputRecord);
                    streamIn.Close();
                    streamIn = null;
                    throw new ApplicationException(msg);
                }
            }
            streamIn.Close();
        }

        public override void Start(uint settleMillisecs, uint sampleMillisecs, double statsDelta)
        {
            Console.WriteLine("PolicyEsses.Start()");
            rateController.SettleMillisecs = this.settleMillisecs = settleMillisecs;
            rateController.SampleMillisecs = this.sampleMillisecs = sampleMillisecs;
            this.cacheAllocControlCounter = 0;
            this.statsDelta = statsDelta;
            //
            // Ask Rate Controller to install the raps in the remote minifilter drivers.
            // At this point the minifilter drivers will start to enforce TX rate limits.
            //
            rateController.InstallRaps();

            //
            // Collect some stats so we have some idea of which edges are active.
            //
            Thread.Sleep(1000);
            rateController.GetStatsFromServers(statsDelta, true);

            //
            // Expect current thread to block on the following until RateController is shutting down.
            //
            rateController.Start();

            //
            // Slaves will cope if we just kill the sockets, but the Rate Controller's Close()
            // routine attempts a gracefull exit by sending explicit delete messages.
            //
            rateController.Close();
        }
        #endregion


        #region callbacks
        public override void CallbackSettleTimeout()
        {
            rateController.ResetStats();
            rateController.SampleMillisecs = sampleMillisecs;   // Optional: can vary if needed
        }
        public override void CallbackControlInterval()
        {
            rateController.GetStatsFromServers(statsDelta, true);
            
            foreach (Flow flow in listFlows)
            {
                if (flow.RapD == null)
                    continue;
                Console.WriteLine("Flow {0} RapD.IoFlowStats {1}", flow.FlowId, flow.RapD.IoFlowStats);
                processFlowCacheStats(flow, flow.RapD.IoFlowStats);
                
                //flow.IoFlowUpdateParams = flow.RapD.IoFlowStats; //XXXIS: do we need this right now?
            }


            UInt64 totalNonIdleCacheSpaceLastInterval = 0;
            UInt64 totalBytesAccessedLastInterval = 0;

            string logMessage = "";
            for (int i = 0; i < listFlows.Count; i++)
            {
                Flow flow = listFlows[i];
                EssesFlowCacheContext fCC = (EssesFlowCacheContext)flow.Context;

                if (fCC.flowBytesAccessedLastSampleInterval != 0)
                {
                    totalNonIdleCacheSpaceLastInterval += fCC.cacheSizeAllocated;
                    totalBytesAccessedLastInterval += fCC.flowBytesAccessedLastSampleInterval;
                }
            }
            logMessage += totalNonIdleCacheSpaceLastInterval + "," + totalBytesAccessedLastInterval;

            rateController.Log(logMessage);

            //string logMessage = "";
            //for (int i = 0; i < listFlows.Count; i++)
            //{
            //    Flow flow = listFlows[i];
            //    EssesFlowCacheContext fCC = (EssesFlowCacheContext)flow.Context;
            //    logMessage += fCC.cacheSizeAllocated + "," + fCC.flowBytesAccessedLastSampleInterval;
            //    if (i != listFlows.Count - 1)
            //    {
            //        logMessage += ",";
            //    }
            //}
            //rateController.Log(logMessage);
            


#if CALCULATE_ALLOCATION
            this.cacheAllocControlCounter++;
            if (this.cacheAllocControlCounter == this.cacheAllocControlFreq)
            {
                //XXXIS: SOPHISTICATED MEMORY ALLOCATION CONTROL ALGORITHM GOES HERE
                calculateMemoryAllocations();
                rateController.UpdateRateLimits();
                this.cacheAllocControlCounter = 0;
            }
#endif
        }

#if MAX_MIN_ALLOCATION
        private void calculateMemoryAllocations()
        {
            foreach (EssesFileCacheContext fileCache in this.fileCaches.Values)
            {
                List<long> weights, demands, rates;
                long epsilon=0;
                weights = new List<long>();
                demands = new List<long>();
                rates = new List<long>();

                foreach (Flow flow in fileCache.flows)
                {
                    EssesFlowCacheContext flowCacheContext = (EssesFlowCacheContext)flow.Context;
                    weights.Add(1);
                    rates.Add(0);
                    //XXXIS: Converting from ulong to long: NOT okay, but will have to do for now...
                    //demands.Add((long)((EssesFlowCacheContext)flow.Context).flowDemand);
                }
                WeightedMinMax(weights, demands, rates, (long)fileCache.fileCacheSize, epsilon);

                int i=0;
                foreach (Flow flow in fileCache.flows)
                {
                    string responseString = "";
                    EssesFlowCacheContext context = (EssesFlowCacheContext)flow.Context;
                    if (rates[i]!=(long)context.cacheSizeAllocated){
                        //Response string example; commands to cache module can be any number of the commands below in the same message (in any order)
                        responseString += "changeCacheSize=" + rates[i].ToString() + " ";
                        context.cacheSizeAllocated = (UInt64) rates[i]; //update our own state of the new flow cache size
                    }

                    //ADD WHATEVER OTHER PARAMETERS YOU WANT TO CHANGE THE CLIENT CACHE HERE
                    flow.IoFlowUpdateParams = responseString; //XXXIS: parameters to update flow get put in responseString
                    i++;
                }
            }
        }

                private void WeightedMinMax(List<long> weights, List<long> demands, List<long> rates, long capacity, long epsilon)
        {
            long leftC = capacity;
            bool progress = true;
            List<long> unallocated = new List<long>();
            for (int i = 0; i < weights.Count; i++)
                unallocated.Add(i);

            // capacity can be a low value such that we cannot even meet the desired epsilon
            // we silently change epsilon accordingly
            long demandTotal = 0;
            foreach (long d in demands)
                demandTotal += d;

            if (capacity < demandTotal)
                epsilon = 0;
            else if ((capacity - demandTotal) < unallocated.Count * epsilon)
                epsilon = (capacity - demandTotal) / unallocated.Count;

            while (unallocated.Count > 0 && progress)
            {
                progress = false;
                long weight = 0;
                foreach (int index in unallocated)
                    weight += weights[index];

                long share = (leftC - unallocated.Count * epsilon) * 1000 / weight;
                Debug.Assert(share >= 0, "Allocated share < 0: ");
                List<int> done = new List<int>();
                foreach (int index in unallocated)
                {
                    long demand = demands[index];
                    if (demand <= (share * weights[index] / 1000))
                    {
                        // This VM's demand can be met.
                        long rate = demand + epsilon; // epsilon allows for ramp-up.
                        Debug.Assert(leftC >= rate);
                        Debug.Assert(rate >= 0);
                        leftC -= rate;
                        rates[index] = (uint)rate;
                        progress = true;
                        done.Add(index);
                    }
                }
                foreach (int index in done)
                    unallocated.Remove(index);
            }

            //
            // Deal with remaining VMs or remaining spare capacity.
            //
            if (unallocated.Count > 0)
            {
                long weight = 0;
                foreach (int index in unallocated)
                    weight += weights[index];

                // The demand for some VMs cannot be met.
                long share = (long)leftC * 1000 / weight;
                foreach (int index in unallocated)
                    rates[index] = (share * weights[index] / 1000);
            }
            else
            {
                // Demands for all VMs have been met.
                // Share the left capacity in a weighted fashion.
                long sumAllocated = (long)capacity - leftC;
                //Debug.Assert(sumAllocated > 0, "sumAllocated <= 0");

                long totalDemand = 0;
                for (int i = 0; i < demands.Count; i++)
                    totalDemand += demands[i];

                List<long> list;
                long total;

#if WEIGHTED_SHARING
                if (totalDemand != 0)
                {
                    list = demands;
                    total = totalDemand;
                }
                else
                {
                    list = weights;
                    total = 0;
                    for (int i = 0; i < weights.Count; i++)
                        total += weights[i];
                }
#else
                list = new List<long>();
                total = weights.Count;
                for (int i = 0; i < weights.Count; i++)
                    list.Add(1);
#endif

                for (int i = 0; i < list.Count; i++)
                {
                    long bonus = (long)(leftC * ((double)list[i] / (double)total));
                    if (bonus > 0)
                        rates[i] += bonus;
                }
            }
        }
#endif

#if UTILITY_MAXIMIZATION
        private void calculateMemoryAllocations()
        {
            foreach (EssesFileCacheContext fileCache in this.fileCaches.Values)
            {
                List<IInterpolation> utilities = new List<IInterpolation>();
                List<UInt64> minGuarantees = new List<UInt64>();
                List<UInt64> finalAllocations = new List<UInt64>();

                UInt64 epsilon = 1048576; //Water-filling constant ; XXIS: PICK A GOOD VALUE FOR THIS

                List<uint> flowsInAllocation = new List<uint>();
                foreach (Flow flow in fileCache.flows)
                {
                    EssesFlowCacheContext flowCacheContext = (EssesFlowCacheContext)flow.Context;

                    if (flowCacheContext.cacheAccessesLastAllocInterval != 0)
                    {
                        utilities.Add(flowCacheContext.cacheDemandFunc);
                        minGuarantees.Add(flowCacheContext.minFlowGuarantee);
                        flowsInAllocation.Add(flow.FlowId);
                    }

                }



                UtilityMaximizationAllocation(fileCache.fileCacheSize, utilities, minGuarantees, epsilon, finalAllocations);

                int i = 0; //counter for flows that were allocated cache this iteration (with flowIDs in flowsInAllocation)
                foreach (Flow flow in fileCache.flows)
                {
                    string responseString = "";
                    EssesFlowCacheContext context = (EssesFlowCacheContext)flow.Context;

                    if (flowsInAllocation.Contains(flow.FlowId))
                    {
                        if (finalAllocations[i] != context.cacheSizeAllocated)
                        {
                            //Response string example; commands to cache module can be any number of the commands below in the same message (in any order)
                            responseString += "changeCacheSize=" + finalAllocations[i].ToString() + " ";
                            context.cacheSizeAllocated = finalAllocations[i]; //update our own state of the new flow cache size

                            //if we changed the cache size, reset the counters to compute hit rate (client is doing so too)
                            context.cacheAccessesTotal = 0;
                            context.cacheAccessesHitsTotal = 0;
                        }
                        i++; //only increment when we see a flow we allocated cache to
                    }
                    else //flow is idle; not included in cache allocation
                    {
                        if (context.cacheSizeAllocated != 0)
                        {
                            responseString += "changeCacheSize=0";
                            context.cacheSizeAllocated = 0; //update our own state of the new flow cache size

                            //if we changed the cache size, reset the counters to compute hit rate (client is doing so too)
                            context.cacheAccessesTotal = 0;
                            context.cacheAccessesHitsTotal = 0;
                        }
                    }

                    //ADD WHATEVER OTHER PARAMETERS YOU WANT TO CHANGE THE CLIENT CACHE HERE
                    flow.IoFlowUpdateParams = responseString; //XXXIS: parameters to update flow get put in responseString
                }
            }
        }

#endif


#if END_TO_END_BW
        private void calculateMemoryAllocations()
        {

            foreach (EssesFileCacheContext fileCache in this.fileCaches.Values)
            {


                List<IInterpolation> utilities = new List<IInterpolation>();
                List<UInt64> minNecessaryAllocations = new List<UInt64>();
                List<UInt64> finalAllocations = new List<UInt64>();

                UInt64 epsilon = 1048576; //Water-filling constant ; XXIS: PICK A GOOD VALUE FOR THIS

                List<uint> flowsInAllocation = new List<uint>();
                foreach (Flow flow in fileCache.flows)
                {
                    EssesFlowCacheContext flowCacheContext = (EssesFlowCacheContext)flow.Context;

                    UInt64 flowBWSLA = flowCacheContext.guaranteedE2EBW;
                    double numerator = (double)(flowBWSLA - this.remoteStorageBW);
                    double deminator = (double)(this.localCacheBW - this.remoteStorageBW);
                                                            
                    double necessaryHitRate = numerator / deminator;
                    
                    Double[] newYVals = new Double[flowCacheContext.cacheDemandCurvePoints.yVals.Length];

                    for (int j=0; j< flowCacheContext.cacheDemandCurvePoints.yVals.Length; j++){
                        newYVals[j]= necessaryHitRate - flowCacheContext.cacheDemandCurvePoints.yVals[j];
                    }

                    Func<double, double> rootFunc = Fit.PolynomialFunc(flowCacheContext.cacheDemandCurvePoints.xVals, newYVals, 10);
                                        

                    //double reqCacheSize = MathNet.Numerics.RootFinding.Bisection.FindRoot(rootFunc, 0, (double)fileCache.fileCacheSize);
                    double reqCacheSize; // = MathNet.Numerics.RootFinding.RobustNewtonRaphson.FindRoot(rootFunc, Differentiate.FirstDerivativeFunc(rootFunc), 0, (double)fileCache.fileCacheSize, 1e-3);

                    if (MathNet.Numerics.RootFinding.RobustNewtonRaphson.TryFindRoot(rootFunc, Differentiate.FirstDerivativeFunc(rootFunc), 0, (double)fileCache.fileCacheSize, 1e-3, 100, 20, out reqCacheSize))
                    {

                    }

                    utilities.Add(flowCacheContext.cacheDemandFunc);
                    minNecessaryAllocations.Add((UInt64)reqCacheSize);
                    flowsInAllocation.Add(flow.FlowId);
                }


                UtilityMaximizationAllocation(fileCache.fileCacheSize, utilities, minNecessaryAllocations, epsilon, finalAllocations);

                int i = 0; //counter for flows that were allocated cache this iteration (with flowIDs in flowsInAllocation)
                foreach (Flow flow in fileCache.flows)
                {
                    string responseString = "";
                    EssesFlowCacheContext context = (EssesFlowCacheContext)flow.Context;

                    if (flowsInAllocation.Contains(flow.FlowId))
                    {
                        if (finalAllocations[i] != context.cacheSizeAllocated)
                        {
                            //Response string example; commands to cache module can be any number of the commands below in the same message (in any order)
                            responseString += "changeCacheSize=" + finalAllocations[i].ToString() + " ";
                            context.cacheSizeAllocated = finalAllocations[i]; //update our own state of the new flow cache size

                            //if we changed the cache size, reset the counters to compute hit rate (client is doing so too)
                            context.cacheAccessesTotal = 0;
                            context.cacheAccessesHitsTotal = 0;
                        }
                        i++; //only increment when we see a flow we allocated cache to
                    }
                    else //flow is idle; not included in cache allocation
                    {
                        if (context.cacheSizeAllocated != 0)
                        {
                            responseString += "changeCacheSize=0";
                            context.cacheSizeAllocated = 0; //update our own state of the new flow cache size

                            //if we changed the cache size, reset the counters to compute hit rate (client is doing so too)
                            context.cacheAccessesTotal = 0;
                            context.cacheAccessesHitsTotal = 0;
                        }
                    }

                    //ADD WHATEVER OTHER PARAMETERS YOU WANT TO CHANGE THE CLIENT CACHE HERE
                    flow.IoFlowUpdateParams = responseString; //XXXIS: parameters to update flow get put in responseString
                }
            }
        }
#endif


        private void UtilityMaximizationAllocation(UInt64 capacity, List<IInterpolation> utilities, List<UInt64> minGuarantees, UInt64 epsilon, List<UInt64> finalAllocations)
        {
            Debug.Assert(utilities.Count == minGuarantees.Count); //Make sure all the lists we got are of equal size... 
            if (utilities.Count == 0)
            {
                return;
            }

            UInt64 usedSoFar = 0;
            for (int i = 0; i < minGuarantees.Count; i++)
            {
                finalAllocations.Add(minGuarantees[i]);
                usedSoFar += minGuarantees[i];
            }

            Debug.Assert(usedSoFar <= capacity); //admission control should guarantee this

            UInt64 leftC = capacity - usedSoFar;

            while (leftC > 0)
            {
                UInt64 cacheAlloc = Math.Min(epsilon, leftC);

                List<int> maxUtilIndices = new List<int>(); //we make it a list, in case of ties
                double maxUtilityGain = 0.0F;

                List<double> diffs = new List<double>();

                for (int i = 0; i < utilities.Count; i++)
                {
                    IInterpolation curDemandCurve = utilities[i];
                    double curUtilityGain = curDemandCurve.Interpolate(finalAllocations[i] + epsilon) - curDemandCurve.Interpolate(finalAllocations[i]);

                    diffs.Add(curUtilityGain);

                    if (Math.Abs(maxUtilityGain - curUtilityGain) < 0.0001)
                    { //if they're practically equal
                        maxUtilIndices.Add(i);
                    }
                    else
                    {
                        if (curUtilityGain > maxUtilityGain)
                        {
                            maxUtilIndices.Clear();
                            maxUtilIndices.Add(i);
                            maxUtilityGain = curUtilityGain;
                        }
                    }
                }

                int allocationWinner = int.MaxValue; //index of the allocation winner this round
                if (maxUtilIndices.Count > 1)
                {
                    UInt64 smallestAllocation = UInt64.MaxValue;
                    foreach (int i in maxUtilIndices)
                    {
                        if (finalAllocations[i] < smallestAllocation)
                        {
                            allocationWinner = i;
                            smallestAllocation = finalAllocations[i];
                        }
                    }
                }
                else if (maxUtilIndices.Count == 1)
                {
                    allocationWinner = maxUtilIndices[0];
                }
                else
                {
                    Debug.Assert(0 == 1); //This shouldn't happen...
                }

                finalAllocations[allocationWinner] += cacheAlloc;
                leftC -= cacheAlloc;
            }
        }

        private void processFlowCacheStats(Flow flow, string statsString)
        {
            //Stats strings look like: "cacheSizeAllocated={0} cacheSizeUsed={1} cacheAccessesTotal={2} flowBytesAccessed={3} cacheAccessesHits={4} cacheEvictions={5} {6}"

            EssesFlowCacheContext fCC = (EssesFlowCacheContext)flow.Context;
            string[] statsStringSeparators = new string[] { " ", "\t" };
            char[] tokenSeparator = new char[] { '=' };
            string[] statsTokens = statsString.Split(statsStringSeparators, StringSplitOptions.RemoveEmptyEntries);
            Debug.Assert(statsTokens.Length > 0);

            //Extract the stats from the string sent by the client slave
            fCC.cacheSizeAllocated = Convert.ToUInt64(statsTokens[0].Split(tokenSeparator)[1]);
            fCC.cacheSizeUsed = Convert.ToUInt64(statsTokens[1].Split(tokenSeparator)[1]);

            UInt64 cacheAccessesNow, flowBytesAccessedNow, cacheAccessesHitsNow, cacheEvictionsNow;

            cacheAccessesNow = Convert.ToUInt64(statsTokens[2].Split(tokenSeparator)[1]);
            flowBytesAccessedNow = Convert.ToUInt64(statsTokens[3].Split(tokenSeparator)[1]);
            cacheAccessesHitsNow = Convert.ToUInt64(statsTokens[4].Split(tokenSeparator)[1]);
            cacheEvictionsNow = Convert.ToUInt64(statsTokens[5].Split(tokenSeparator)[1]);

            Debug.Assert(cacheAccessesNow >= fCC.cacheAccessesTotal);
            Debug.Assert(flowBytesAccessedNow >= fCC.flowBytesAccessedTotal);
            Debug.Assert(cacheAccessesHitsNow >= fCC.cacheAccessesHitsTotal);

            if (cacheAllocControlCounter == 0)
            {
                fCC.cacheAccessesLastAllocInterval = 0;
                fCC.flowBytesAccessedLasAllocInterval = 0;
                fCC.cacheAccessesHitsLastAllocInterval = 0;
                fCC.cacheEvictionsLastAllocInterval = 0;
            } 
            else if (cacheAllocControlCounter < cacheAllocControlFreq)
            {
                fCC.cacheAccessesLastAllocInterval += (cacheAccessesNow - fCC.cacheAccessesTotal);
                fCC.flowBytesAccessedLasAllocInterval += (flowBytesAccessedNow - fCC.flowBytesAccessedTotal);
                fCC.cacheAccessesHitsLastAllocInterval += (cacheAccessesHitsNow - fCC.cacheAccessesHitsTotal);
                fCC.cacheEvictionsLastAllocInterval += (cacheEvictionsNow - fCC.cacheEvictionsTotal);
            }

            fCC.flowBytesAccessedLastSampleInterval = flowBytesAccessedNow - fCC.flowBytesAccessedTotal;
            
            fCC.cacheAccessesTotal = cacheAccessesNow;
            fCC.flowBytesAccessedTotal = flowBytesAccessedNow;
            fCC.cacheAccessesHitsTotal = cacheAccessesHitsNow;
            fCC.cacheEvictionsTotal = cacheEvictionsNow;

            fCC.hitRate = (fCC.cacheAccessesTotal > 0 ? (float)fCC.cacheAccessesHitsTotal / (float)fCC.cacheAccessesTotal : 0.0F);

            if (statsTokens.Length > 6) //cache demand string is included
            {
                string[] cacheDemandPairs = statsTokens[6].Split(';');
                Double[] xVals = new Double[cacheDemandPairs.Length];
                Double[] yVals = new Double[cacheDemandPairs.Length];
                for (int i = 0; i < cacheDemandPairs.Length; i++)
                {
                    xVals[i] = Convert.ToDouble(cacheDemandPairs[i].Split(',')[0]);
                    yVals[i] = Convert.ToDouble(cacheDemandPairs[i].Split(',')[1]);
                }

                if (!fCC.useSerializedCurve) //only save curve if we don't have a curve at the moment
                {
                    fCC.cacheDemandCurvePoints = new CacheCurvePoints(xVals, yVals);
                    //fCC.cacheDemandFunc = Interpolate.LinearBetweenPoints(xVals, yVals);
                    //fCC.cacheDemandFunc = Interpolate.Linear(xVals, yVals);
                    fCC.cacheDemandFunc = Interpolate.PolynomialEquidistant(xVals, yVals);

                }

#if COLLECT_CACHE_CURVE
                //////////XXXIS: quick test to see if we got the function fitted properly (since graphing's a pain...)

                //XXXIS change fileCacheSize accordingly to print the curve on the screen, IN ADDITION to it being collected when you Ctrl+C the
                //controller at the end of the run

                UInt64 fileCacheSize = 10737418240;
                int numCurvePoints = 500;
                float stepSize = 1.0F / (float)numCurvePoints;
                Console.WriteLine("Flow ID {0}", flow.FlowId);

                for (float frac = 0.0F; frac <= (1.0F + stepSize); frac += stepSize)
                {
                    UInt32 ghostCacheBlockSize = 4096;
                    //Compute and block-align the cache sizes; XXXIS: THIS ASSUMES ghost cache block size is a power of two!!!
                    UInt64 specCacheSize = ((UInt64)(frac * (float)fileCacheSize)) & (~((UInt64)(ghostCacheBlockSize - 1)));
                    Console.WriteLine("{0}, {1}", specCacheSize, fCC.cacheDemandFunc.Interpolate((double)specCacheSize));
                    //rateController.Log(String.Format("{0}, {1}", specCacheSize, fCC.cacheDemandFunc.Interpolate((double)specCacheSize)));
                }
#endif
            }

            //Console.WriteLine("Flow {0}, SizeAllocated {1}, SizeUsed {2}, flowBytesAccessedTotal {3}, AccessesTotal {4}, AccessesHits {5}, Evictions {6}, HitRate {7}",
            //    flow.FlowId, fCC.cacheSizeAllocated, fCC.cacheSizeUsed, fCC.flowBytesAccessedTotal, fCC.cacheAccessesTotal,
            //    fCC.cacheAccessesHitsTotal, fCC.cacheEvictionsTotal, fCC.hitRate);
        }


        /// <summary>
        /// Concurrent threads may ask us to handle an unsolicited alert.
        /// Push the alert to the RateController for threadsafe callback on our CallBackAlert() routine.
        /// The alert is serialized at source as if the caller was remote and TCP delivered the message.
        /// </summary>
        /// <param name="alertType"></param>
        /// <param name="args"></param>
        /// 
        public override void PostAlert(OktoAlertType alertType, ulong[] args)
        {
            rateController.PostAlert(alertType, args);
        }

        public override void PostAlertMessage(byte[] buffer, int offset)
        {
            rateController.PostAlertMessage(buffer, 0);
        }
        public override void CallbackAlert(MessageAlert messageAlert, RateControllerState state)
        {
            ulong[] Args = messageAlert.Args;
            //switch (messageAlert.AlertType)
            //{
            //    case OktoAlertType.AlertDemoGui:
            //        switch ((AlertDemoGui)Args[0])
            //        {
            //            case AlertDemoGui.ChangeLimitTokenSec:
            //                throw new NotImplementedException();
            //                break;
            //            case AlertDemoGui.ChangePolicy:
            //                throw new NotImplementedException();
            //                break;
            //            case AlertDemoGui.SetCapacity:
            //                throw new NotImplementedException();
            //                break;
            //            case AlertDemoGui.SetIsAlgorithmEnabled:
            //                throw new NotImplementedException();
            //                break;
            //            case AlertDemoGui.ChangePriority:
            //                throw new NotImplementedException();
            //                break;
            //            case AlertDemoGui.SetControlInterval:
            //                throw new NotImplementedException();
            //                break;
            //            case AlertDemoGui.SetVecIdx:
            //                throw new NotImplementedException();
            //                break;
            //            default:
            //                throw new ArgumentOutOfRangeException("AlertDemoGui unhandled " + Args[0].ToString());
            //        }
            //        break;

            //    default:
            //        throw new NotImplementedException();
            //        break;
            //}
            string logmsg = string.Format("CallbackAlert {0} received alert type {1} args:", DateTime.Now, messageAlert.AlertType.ToString());
            for (int i = 0; i < Args.Length; i++)
                logmsg += Args[i].ToString() + ",";
            rateController.Log(logmsg);
        }
        #endregion

        #region overrides required by abstract class "OktofsPolicy"
        public override String DynamicCTraceMessage(int resIndex)
        {
            throw new NotImplementedException();
        }
        public override List<long> GetCt()
        {
            throw new NotImplementedException();
        }


        #endregion


        public override void Shutdown()
        {
            shutdownEvent.Set();
            Console.WriteLine("PolicyEsses shutting down.");
            rateController.Close();
            Process.GetCurrentProcess().Kill();
            throw new Exception("admin just shut it down.");
        }


    }
}
