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
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32;

namespace OktofsRateControllerNamespace
{
    /// <summary>
    /// Get time at usec precision from Win32 QueryPerformanceCounter.
    /// </summary>
    public class Qpc
    {
        public long QpcFrequency = 0;
        public long QpcTicksPerUs = 0;
        public long QpcCached = 0;
        private long StartTime = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool QueryPerformanceFrequency(
            out long lpFrequency);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool QueryPerformanceCounter(
            out long lpPerformanceCount);

        public Qpc()
        {
            if (QueryPerformanceFrequency(out QpcFrequency) == false)
                throw new ApplicationException("QPCFreq err=" + Marshal.GetLastWin32Error().ToString());
            if (QpcFrequency == 0)
                throw new ApplicationException("Error: QPCFreq is zero.");
            QpcTicksPerUs = QpcFrequency / 1000000;

            if (QueryPerformanceCounter(out StartTime) == false)
                throw new ApplicationException("QPC err=" + Marshal.GetLastWin32Error().ToString());
            Console.WriteLine("QPC frequency={0} qpc={1}", QpcFrequency, StartTime);
        }

        /// <summary>
        /// Returns clock time in microseconds, guaranteed monotonic across all CPUs.
        /// </summary>
        /// <returns>The current clock value in microseconds.</returns>
        public long TimeNowUSecs()
        {
            long QpcNow;
            if (QueryPerformanceCounter(out QpcNow) == false)
                throw new ApplicationException("QPC err=" + Marshal.GetLastWin32Error().ToString());
            return (QpcNow - StartTime) / QpcTicksPerUs;
        }

    }
}
