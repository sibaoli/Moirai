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

namespace OktofsRateControllerNamespace
{
    /// <summary>
    /// This class represents a "stage" or (RAP,Flow) pair in an oktofs Minifilter driver.
    /// </summary>
    public class RAP
    {
        private OktoQueue queue = null;                     // Queue attached to this entry.
        private Device device = null;
        public readonly Endpoint locEndpointDetails;
        public FlowStats flowStats = new FlowStats();
        public string ioFlowStats;

        //
        // Properties exposed to policy modules.
        //
        public OktoQueue Queue { get { return queue; } set { queue = value; } }
        public Device Device { get { return device; } set { device = value; } }
        public string SidOrAccount { get { return locEndpointDetails.SidOrAccount; } }
        public string ShareOrVolume { get { return locEndpointDetails.ShareOrVolume; } }
        public Endpoint LocEndpointDetails { get { return locEndpointDetails; } }
        public int i_index { get { return locEndpointDetails.Index; } }
        public int j_index { get { return 0; } }
        public string IoFlowStats { get { return ioFlowStats; } }

        //
        // Properties for DemoGui : do we really need *all* of these?
        //
        public ulong StatsFlowQLenReadTokensTick { get { return SumOf(flowStats.QLenRead); } }
        public ulong StatsFlowQLenWriteTokensTick { get { return SumOf(flowStats.QLenWrite); } }
        public ulong StatsFlowInFlightEventReadTokens { get { return SumOf(flowStats.InFlightRead); } }
        public ulong StatsFlowInFlightEventWriteTokens { get { return SumOf(flowStats.InFlightWrite); } }

        private ulong SumOf(ulong[] vec)
        {
            ulong result = 0;
            for (int i = 0; i < vec.Length; i++)
                result += vec[i];
            return result;
        }

        internal RAP(
            Endpoint locEndpointDetails,
            Endpoint remEndpointDetails)
        {
            this.locEndpointDetails = locEndpointDetails;
        }

        public bool AssignQueue(OktoQueue queue)
        {
            if (Queue != null)
                throw new ApplicationException("Attempt to assign queue to matrix entry where queue already assigned.");
            Queue = queue;
            queue.listRap.Add(this);
            return true;
        }

        internal void SetFlowStats(FlowStats flowStats)
        {
            this.flowStats = flowStats;
        }

        internal void SetIoFlowStats(string ioFlowStats)
        {
            this.ioFlowStats = ioFlowStats;
        }
    }
}
