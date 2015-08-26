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
using RateControllerNamespace;

namespace OktofsRateControllerNamespace
{
    // Must be wire compatible with E:\winblue_gdr\base\fs\miniFilters\oktofs\inc\oktofsuser.h struct FLOW_STATS
    public class FlowStats
    {
        public ulong CountIopsRead;
        public ulong CountIopsWrite;
        public ulong[] TokSecRead = new ulong[Parameters.OKTO_MAX_VEC_LEN];
        public ulong[] TokSecWrite = new ulong[Parameters.OKTO_MAX_VEC_LEN];
        public ulong[] QLenRead = new ulong[Parameters.OKTO_MAX_VEC_LEN];
        public ulong[] QLenWrite = new ulong[Parameters.OKTO_MAX_VEC_LEN];
        public ulong[] InFlightRead = new ulong[Parameters.OKTO_MAX_VEC_LEN];
        public ulong[] InFlightWrite = new ulong[Parameters.OKTO_MAX_VEC_LEN];

        public static FlowStats CreateFromNetBytes(byte[] buffer, int offset, out int newOffset)
        {
            FlowStats flowStats = new FlowStats();
            flowStats.CountIopsRead = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
            flowStats.CountIopsWrite = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
            for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
            {
                flowStats.TokSecRead[VecIdx] = (ulong)Utils.Int64FromNetBytes(buffer, offset); 
                offset += 8;
            }
            for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
            {
                flowStats.TokSecWrite[VecIdx] = (ulong)Utils.Int64FromNetBytes(buffer, offset);
                offset += 8;
            }
            for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
            {
                flowStats.QLenRead[VecIdx] = (ulong)Utils.Int64FromNetBytes(buffer, offset);
                offset += 8;
            }
            for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
            {
                flowStats.QLenWrite[VecIdx] = (ulong)Utils.Int64FromNetBytes(buffer, offset);
                offset += 8;
            }
            for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
            {
                flowStats.InFlightRead[VecIdx] = (ulong)Utils.Int64FromNetBytes(buffer, offset);
                offset += 8;
            }
            for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
            {
                flowStats.InFlightWrite[VecIdx] = (ulong)Utils.Int64FromNetBytes(buffer, offset);
                offset += 8;
            }
            newOffset = offset;
            return flowStats;
        }
    }

    /// <summary>
    /// Represents the end-to-end path between a VM and a storage volume, 
    /// including OktoQueue objects representing rate limiters at either 
    /// or both of points C and H (Hyper-V and Storage servers stacks, 
    /// respectively). This is the class over which Policy Module code
    /// will most likely iterate to monitor traffic stats and to set
    /// rate limits against the embedded OktoQueue objects.
    /// </summary>
    public class Flow
    {
        private readonly string vmName;
        private readonly string hypervName;        // Hyper-V server where VM in question resides.
        private readonly string shareName;
        private readonly string storageServerName; // Server running (VM,share) filter ("C").
        private readonly string volumeName;
        public int index = -1;
        private string stringSid = null;
        private readonly uint flowId;
        private readonly Endpoint endpointC = null;
        private readonly Endpoint endpointH = null;
        private RAP rapB = null;  // IoFlow just above C (Hypervisor).
        private RAP rapC = null;  // Oktofs at C (Hypervisor).
        private RAP rapH = null;  // Oktofs at H (storage server).
        private byte flags = 0;
        private byte priority = 0x00;
        private readonly string inputRecord = null;
        private readonly string[] inputTokens;
        private readonly int inputTokensLastHeaderIndex;
        private static uint newFlowId = 1;
        private TrafficMatrixEntry tme = null;
        private object context = null;               // Where policy modules can keep extra per-flow state.

        public string VmName { get { return vmName; } }
        public string HypervName { get { return hypervName; } }
        public string ShareName { get { return shareName; } }
        public string StorageServerName { get { return storageServerName; } }
        public string VolumeName { get { return volumeName; } }
        public string StringSid { get { return stringSid; } set { stringSid = value; } }
        public uint FlowId { get { return flowId; } }
        public string IoFlowUpdateParams = "";
        public Endpoint EndpointC { get { return endpointC; } }
        public Endpoint EndpointH { get { return endpointH; } }
        public RAP RapD { get { return rapB; } set { rapB = value; } }
        public RAP RapC { get { return rapC; } set { rapC = value; } }
        public RAP RapH { get { return rapH; } set { rapH = value; } }
        public string InputRecord { get { return inputRecord; } }
        public string[] InputTokens { get { return inputTokens; } }
        public int InputTokensLastHeaderIndex { get { return inputTokensLastHeaderIndex; } }
        public TrafficMatrixEntry TME { get { return tme; } }
        public object Context { get { return context; } set { context = value; } }

        //
        // Stats are obtained from the from embedded TrafficMatrixEntries.
        //
        public ulong[] StatsTokSecVecWrite
        {
            get
            {
                ulong[] result = new ulong[Parameters.OKTO_MAX_VEC_LEN];
                if (tme != null)
                    result[0] += tme.StatsBytesPerSecTx;
                if (rapC != null)
                    for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
                        result[VecIdx] += rapC.flowStats.TokSecWrite[VecIdx];
                if (rapH != null)
                    for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
                        result[VecIdx] += rapH.flowStats.TokSecWrite[VecIdx];
                return result;
            }
        }

        public ulong[] StatsTokSecVecRead
        {
            get
            {
                ulong[] result = new ulong[Parameters.OKTO_MAX_VEC_LEN];
                if (tme != null)
                    result[0] += tme.StatsBytesPerSecRx;
                if (rapC != null)
                    for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
                        result[VecIdx] += rapC.flowStats.TokSecRead[VecIdx];
                if (rapH != null)
                    for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
                        result[VecIdx] += rapH.flowStats.TokSecRead[VecIdx];
                return result;
            }
        }

        public ulong StatsIopsWrite
        {
            get
            {
                ulong result = 0;
                if (tme != null)
                    result += 0;
                if (rapC != null)
                    result += rapC.flowStats.CountIopsWrite;
                if (rapH != null)
                    result += rapH.flowStats.CountIopsWrite;
                return result;
            }
        }
        public ulong StatsIopsRead
        {
            get
            {
                ulong result = 0;
                if (tme != null)
                    result += 0;
                if (rapC != null)
                    result += rapC.flowStats.CountIopsRead;
                if (rapH != null)
                    result += rapH.flowStats.CountIopsRead;
                return result;
            }
        }

        public ulong StatsFlowQLenReadTokensTick
        {
            get { return (rapC == null ? 0 : rapC.StatsFlowQLenReadTokensTick) + (rapH == null ? 0 : rapH.StatsFlowQLenReadTokensTick); }
        }
        public ulong StatsFlowQLenWriteTokensTick
        {
            get { return (rapC == null ? 0 : rapC.StatsFlowQLenWriteTokensTick) + (rapH == null ? 0 : rapH.StatsFlowQLenWriteTokensTick); }
        }
        public ulong StatsFlowInFlightEventReadTokens
        {
            get { return (rapC == null ? 0 : rapC.StatsFlowInFlightEventReadTokens) + (rapH == null ? 0 : rapH.StatsFlowInFlightEventReadTokens); }
        }
        public ulong StatsFlowInFlightEventWriteTokens
        {
            get { return (rapC == null ? 0 : rapC.StatsFlowInFlightEventWriteTokens) + (rapH == null ? 0 : rapH.StatsFlowInFlightEventWriteTokens); }
        }

        /// <summary>
        /// Rate limits are specified on a flow by setting a value on this property.
        /// </summary>
        public ulong[] TokSecVec
        {
            get
            {
                ulong[] result = new ulong[Parameters.OKTO_MAX_VEC_LEN];
                if (tme != null)
                    throw new ApplicationException("TokSecVec not presently supported on network flows");
                else if (rapC != null && rapH != null)
                    throw new ApplicationException("TokSecVec not presently supported when rapC and rapH both in use.");
                else if (rapC != null)
                    for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
                        result[VecIdx] += rapC.Queue.TokSecVec[VecIdx];  
                else if (rapH != null)
                    for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
                        result[VecIdx] += rapH.Queue.TokSecVec[VecIdx];
                return result;
            }
            set
            {
                if (tme != null)
                    throw new ApplicationException("TokSecVec not presently supported on network flows");
                else if (rapC != null && rapH != null)
                    throw new ApplicationException("TokSecVec not presently supported when rapC and rapH both in use.");
                else if (rapC == null)
                    rapH.Queue.TokSecVec = value;
                else if (rapH == null)
                    rapC.Queue.TokSecVec = value;
                else
                {
                    // Base case : throttle at C not at H.
                    rapC.Queue.TokSecVec = value;
                    ulong[] NoThrottle = new ulong[Parameters.OKTO_MAX_VEC_LEN];
                    for (uint VecLen = 0; VecLen < Parameters.OKTO_MAX_VEC_LEN; VecLen++)
                        NoThrottle[VecLen] = ulong.MaxValue;
                    rapH.Queue.TokSecVec = NoThrottle;
                }
            }
        }


        /// <summary>
        /// Token Bucket capacity is specified on a flow by setting a value on this property.
        /// </summary>
        public ulong[] BucketCapacity
        {
            get
            {
                ulong[] result = new ulong[Parameters.OKTO_MAX_VEC_LEN];
                if (tme != null)
                    throw new ApplicationException("BucketCapacity not presently supported on network flows");
                else if (rapC != null && rapH != null)
                    throw new ApplicationException("BucketCapacity not presently supported when rapC and rapH both in use.");
                else if (rapC != null)
                    for (uint VecLen = 0; VecLen < Parameters.OKTO_MAX_VEC_LEN; VecLen++)
                        result[VecLen] += rapC.Queue.BucketCapacity[VecLen];
                else if (rapH != null)
                    for (uint VecLen = 0; VecLen < Parameters.OKTO_MAX_VEC_LEN; VecLen++)
                        result[VecLen] += rapH.Queue.BucketCapacity[VecLen];
                return result;
            }
            set
            {
                if (tme != null)
                    throw new ApplicationException("BucketCapacity not presently supported on network flows");
                else if (rapC != null && rapH != null)
                    throw new ApplicationException("BucketCapacity not presently supported when rapC and rapH both in use.");
                else if (rapC == null)
                    rapH.Queue.BucketCapacity = value;
                else if (rapH == null)
                    rapC.Queue.BucketCapacity = value;
                else
                {
                    // Base case : throttle at C not at H.
                    rapC.Queue.BucketCapacity = value;
                    ulong[] NoThrottle = new ulong[Parameters.OKTO_MAX_VEC_LEN];
                    for (uint VecLen = 0; VecLen < Parameters.OKTO_MAX_VEC_LEN; VecLen++)
                        NoThrottle[VecLen] = ulong.MaxValue;
                    rapH.Queue.BucketCapacity = NoThrottle;
                }
            }
        }


        //
        // Priority set via this property, which encapsulates one or more TME->Queue.
        //
        public byte Priority
        {
            get { return priority; }
            set
            {
                priority = value;
                if (rapC != null)
                    rapC.Queue.Priority = value;
                if (rapH != null)
                    rapH.Queue.Priority = value;
            }
        }

        public byte Flags
        {
            get { return flags; }
        }

        public bool IsFlagOn(OktoFlowFlags flag)
        {
            return ((flags & (byte)flag) != 0);
        }

        public void SetFlagOn(OktoFlowFlags flag)
        {
            flags |= (byte)flag;
            if (rapC != null)
                rapC.Queue.SetFlagOn(flag);
            if (rapH != null)
                rapH.Queue.SetFlagOn(flag);
        }

        public void SetFlagOff(OktoFlowFlags flag)
        {
            if (IsFlagOn(flag))
            {
                byte bFlag = (byte)flag;
                flags &= ((byte)~bFlag);
                if (rapC != null)
                    rapC.Queue.SetFlagOff(flag);
                if (rapH != null)
                    rapH.Queue.SetFlagOff(flag);
            }
        }


        public override string ToString()
        {
            return String.Format("Flow {0} from {1} to {2}",
                this.flowId, this.EndpointC, this.endpointH);
        }


        public Flow(
            Endpoint c,
            Endpoint h,
            string inputRecord,
            string[] inputTokens,
            int inputTokensLastHeaderIndex,
            OktofsRateController rateController)
        {
            if (c == null && h == null)
                throw new ArgumentOutOfRangeException(String.Format("Flow: both endpoints cannot be null."));

            this.flowId = newFlowId++;
            this.vmName = (c != null ? c.SidOrAccount.ToLower() : h.SidOrAccount.ToLower());
            this.hypervName = (c != null ? c.FilterServer.ToLower() : h.HyperVserver.ToLower());
            this.shareName = (c != null ? c.ShareOrVolume.ToLower() : null);
            this.storageServerName = (h != null ? h.FilterServer.ToLower() : null);
            this.volumeName = (h != null ? h.ShareOrVolume.ToLower() : null);
            this.endpointC = c;
            this.endpointH = h;
            this.inputRecord = inputRecord;
            this.inputTokens = inputTokens;
            this.inputTokensLastHeaderIndex = inputTokensLastHeaderIndex;

            if (String.IsNullOrEmpty(vmName) || String.IsNullOrWhiteSpace(vmName))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg vmName={0}", vmName));
            if (String.IsNullOrEmpty(hypervName) || String.IsNullOrWhiteSpace(hypervName))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg hypervName={0}", hypervName));
            if (endpointC != null && (String.IsNullOrEmpty(shareName) || String.IsNullOrWhiteSpace(shareName)))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg shareName={0}", shareName));
            if (endpointH != null && (String.IsNullOrEmpty(storageServerName) || String.IsNullOrWhiteSpace(storageServerName)))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg storageServerName={0}", storageServerName));
            if (endpointH != null && (String.IsNullOrEmpty(volumeName) || String.IsNullOrWhiteSpace(volumeName)))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg volumeName={0}", volumeName));
            if (inputTokensLastHeaderIndex < 4)
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg inputTokensLastHeaderIndex too small {0}", inputTokensLastHeaderIndex));
        }

        public Flow(
            TrafficMatrixEntry tme,
            string inputRecord,
            string[] inputTokens,
            int inputTokensLastHeaderIndex)
        {
            this.tme = tme;
            this.inputRecord = inputRecord;
            this.inputTokens = inputTokens;
            this.inputTokensLastHeaderIndex = inputTokensLastHeaderIndex;
        }


        /// <summary>
        /// Default config given C+H is throttle at C and take stats at H.
        /// Delay setting this up until after the TMEs have been created and connected.
        /// </summary>
        public void SetDefaultFlags()
        {
            if (rapC == null && rapH == null && rapB == null)
                throw new ArgumentOutOfRangeException(String.Format("Flow: at least one rap BCH must be non-null."));
            else if (rapC != null && rapH != null)
            {
#if gregos // 20131002 given C+H take flow stats from C (was H) so Dynamic C experiments can see flow queue lengths
                RapC.Queue.SetFlagOn(OktoFlowFlags.FlowFlagNoStats);
#else
                RapH.Queue.SetFlagOn(OktoFlowFlags.FlowFlagNoStats);
#endif
            }
            else if (rapC == null && rapH != null)
            {
                //
                // Rate limiting at H gets really choppy if fastpath enabled under very high Irp MPL.
                // High MPL can generate many more async IRP than OS has workitems to service them.
                // Disabling fastpath when rate limiting at H improves backpressure and limits the
                // number of IRP in NTFS - works smoothly at of lower IRP ceiling 620K->440K (RAMD).
                //
                SetFlagOn(OktoFlowFlags.FlowFlagNoFastpath);
            }
        }
    } // class Flow
} // namespace
