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
    /// Represents a device, typically "\Device\Mup" at C or e.g. "\Device\HarddiskVolumeN" at H.
    /// These are the devices to which the oktofs minifilter is bound, as in "fltmc instances".
    /// </summary>
    public class Device
    {
        public struct IRP_COUNTERS
        {
            public struct READWRITE
            {
                public ulong Iops;
                public ulong Tokens;
            }
            public READWRITE Read;
            public READWRITE Write;
        }

        public IRP_COUNTERS AveInFlightTick;        // Mean in flight below device : sampled per timer tick.
        public IRP_COUNTERS AveInFlightEvent;       // Mean in flight below device : sampled per op (fastpath,queue,dequeue)
        public IRP_COUNTERS AveQueueLenTick;        // Mean queue length : QLen sampled per timer tick.
        public IRP_COUNTERS AveQueueLenEvent;       // Mean queue length : QLen sampled per op (fastpath,queue,dequeue)
        private string deviceName;
        private string hostName;
        public string DeviceName { get { return deviceName; } }
        public string HostName { get { return hostName; } }
        public string FullName { get { return @"\\" + hostName + deviceName; } }

        public Device(string hostName, string deviceName)
        {
            this.hostName = hostName;
            this.deviceName = deviceName;
        }

    }
}
