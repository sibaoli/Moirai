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
using System.Threading;
using System.Linq;
using System.Text;


namespace OktofsRateControllerNamespace
{
    public abstract class OktofsPolicy : IPolicyModule, IDisposable
    {
        public OktofsRateController rateController;          // Reference to core Rate Controller instance.
        public ManualResetEvent shutdownEvent;               // Main thread blocks pending shutdown request.
        public DemoGuiStats demoGuiStats = new DemoGuiStats();

        // DEMO
        public abstract List<long> GetCt();
        public virtual List<string> GetResourceIds() { return new List<string>() { "all" }; }
        public virtual DemoGuiStats GetDemoGuiStats()
        {
            return demoGuiStats;
        }

        // CONTROLLER
        public abstract void Start(uint settleMillisecs, uint sampleMillisecs, double statsDelta);
        public abstract void Shutdown();
        public abstract void PostAlert(OktoAlertType alertType, ulong[] args);
        public abstract void PostAlertMessage(byte[] buffer, int offset);
        public abstract void CallbackSettleTimeout();
        public abstract void CallbackControlInterval();
        public abstract void CallbackAlert(MessageAlert messageAlert, RateControllerState state);
        public virtual void Log(string logRec) { rateController.Log(logRec); }

        // Dynamic Estimation
        public abstract String DynamicCTraceMessage(int resIndex);

        #region IDisposable // per CodeAnalysis
        //
        // CodeAnalysis requires we implement IDisposable due to use of ManualResetEvent
        //
        private bool disposed = false;

        public virtual void Close()
        {
            Dispose();
        }

        /// <summary>
        /// Ref documentation for IDisposable interface.
        /// Do not make this method virtual.
        /// A derived class should not be able to override this method.
        /// </summary>
        public virtual void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SupressFinalize to
            // take this object off the finalization queue
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        // Dispose(bool disposing) executes in two distinct scenarios.
        // If disposing equals true, the method has been called directly
        // or indirectly by a user's code. Managed and unmanaged resources
        // can be disposed.
        // If disposing equals false, the method has been called by the
        // runtime from inside the finalizer and you should not reference
        // other objects. Only unmanaged resources can be disposed.
        public virtual void Dispose(bool calledByUserCode)
        {
            // Check to see if Dispose has already been called.
            if (!this.disposed)
            {
                if (calledByUserCode)
                {
                    //
                    // Dispose managed resources here.
                    //
                    if (rateController != null)
                        rateController.Close();
                }

                // 
                // Dispose unmanaged resources here.
                //
                if (shutdownEvent != null)
                {
                    shutdownEvent.Close();
                    shutdownEvent = null;
                }

            }
            disposed = true;
        }

        // Use C# destructor syntax for finalization code.
        // This destructor will run only if the Dispose method 
        // does not get called.
        ~OktofsPolicy()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }
        #endregion
    }

    public class DemoGuiStats
    {
        public List<ulong[]> SnapDemoGuiStatsList = new List<ulong[]>();
        public ulong[] SnapDemoGuiFlowTokInFlight = new ulong[2];   // Sum device QLen tokens per second across all C.
        public ulong[] SnapDemoGuiFlowTokQueued = new ulong[2];     // Sum tokens queued across all flows.
        public List<string> listResourceDetails = new List<string>(); // Free format details.
    }

}
