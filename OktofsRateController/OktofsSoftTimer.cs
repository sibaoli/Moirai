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
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

namespace OktofsRateControllerNamespace
{
    /// <summary>
    /// </summary>

    internal class SoftTimer
    {
        private object SoftTimerLock = new object();
        internal SortedDictionary<long, SoftTimerEntry> queue;
        OktofsRateController rateController = null;
        System.Timers.Timer TimersTimer = null;
        private DateTime StartDateTime = DateTime.Now;

        /// <summary>
        /// </summary>
        /// <param name="maxQueueLength">Max number of entries in timer queue.</param>
        /// <param name="frequency">Check the queue once every n ticks.</param>
        public SoftTimer(int maxQueueLength, OktofsRateController rateController)
        {
            this.rateController = rateController;
            queue = new SortedDictionary<long, SoftTimerEntry>();
            const double dTenMillisecs = 10.0;
            TimersTimer = new System.Timers.Timer(dTenMillisecs);
            TimersTimer.Elapsed += new ElapsedEventHandler(TimersTimerCallback);
            TimersTimer.Interval = dTenMillisecs;
            TimersTimer.Enabled = true;
        }

        /// <summary>
        /// Returns clock time in microseconds.
        /// </summary>
        /// <returns>The current clock value in microseconds.</returns>
        public long TimeNowUSecs()
        {
            TimeSpan timeSpan = DateTime.Now - StartDateTime;
            long timeMSecs = (long)timeSpan.TotalMilliseconds;
            return timeMSecs * 1000;
        }

        /// <summary>
        /// Create a new entry on the timer queue. Caller must not provide a time
        /// that has already been registered.
        /// </summary>
        /// <param name="uSecs">Time (absolute) expressed in usecs.</param>
        /// <param name="appContext">Ref to a context object supplied by the caller.</param>
        internal void RegisterTimer(long uSecTime, object appContext)
        {
            SoftTimerEntry ste = new SoftTimerEntry(uSecTime, appContext);
            lock (SoftTimerLock)
            {
                Debug.Assert(queue.ContainsKey(uSecTime) == false);
                queue.Add(uSecTime, ste);
            }
        }

        private void TimersTimerCallback(object source, ElapsedEventArgs e)
        {
            SoftTimerEntry ste;

            lock (SoftTimerLock)
            {
                TimersTimer.Enabled = false;
                // Allowed to fire multiple events from queue.
                while ((ste = GetFirstExpired()) != null)
                {
                    // Push directly to RateController's sequential work queue.
                    rateController.RcWorkQueue.Enqueue((OktofsRateControllerNamespace.OktofsRateController.RcWorkItem)ste.AppContext);
                    Free(ste);
                }
                TimersTimer.Enabled = true;
            }
        }

        public void Close()
        {
        }

        /// <summary>
        /// Returns the first expired entry on the list, if any.
        /// </summary>
        /// <returns>Returns the first expired entry on the list, if any.</returns>
        public SoftTimerEntry GetFirstExpired()
        {
            SoftTimerEntry ste = null;

            lock (SoftTimerLock)
            {
                // Find the first (lowest ordered) item on the queue.
                if (queue.Count > 0)
                    ste = queue.First().Value;

                if (ste != null && ste.USecTime <= TimeNowUSecs())
                    queue.Remove(ste.USecTime);
                else
                    ste = null;
            }

            return ste;
        }

        /// <summary>
        /// Return an expired timer entry to the free pool.
        /// </summary>
        /// <param name="ste"></param>
        public void Free(SoftTimerEntry ste)
        {
            // no-op.
        }

    }

    class SoftTimerEntry
    {
        internal object AppContext;
        internal long USecTime;

        public SoftTimerEntry(long uSecTime, object appContext)
        {
            AppContext = appContext;
            USecTime = uSecTime;
        }
    }

}
