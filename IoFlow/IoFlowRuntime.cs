//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************
#define UseReplyAndGetNext

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;
using BridgeOktofsAgentNameSpace;


namespace IoFlowNamespace
{
    /// <summary>
    /// Exposes a managed-code interface to the ioflowuser minifilter driver.
    /// Internally it uses the fltmgr "message" protocol to exchange representations
    /// of IRPs for a subset of IRP types with the ioflow.sys minifilter driver. 
    /// </summary>
    public class IoFlowRuntime : IDisposable
    {
        private bool disposed = false;                            // VS Code Analysis tool says we must implement IDispose.
        private SafeFileHandle hDataAsyncPort = null;             // Filter message comms on data plane (IRP upcalls).
        private SafeFileHandle hControlPort = null;               // Filter message comms on control plane (e.g. CreateFlow).
        private bool ShuttingDown = false;                        // Controls transition to shutdown mode.
        private IntPtr hCompletionPort = IntPtr.Zero;             // Handle to I/O completion port.
        private uint countIocpThreads = (uint)System.Environment.ProcessorCount;
        private Thread[] arrayIocpThreads = null;
        private IRP[] irpArray = null;
        private IRP FreeIrpPool = null;
        private int countFreeIrpList = 0;
        private object IrpPoolLock = new object();
        private int CountIocpThreadsRunning = 0;
        private ManualResetEvent LastThreadTerminated;            // Shutdown.
        private Dictionary<IntPtr, IRP> FindIrp;                  // Find Irp given Win32 address of Irp.OVERLAPPED.
        private uint TenantId;
        private Dictionary<uint, IoFlow> DictIoFlow;
        private Dictionary<string, string> DriveNameToVolumeName;
        private Dictionary<string, string> VolumeNameToDriveName;
        public const string DeviceHarddiskVolume = @"\device\harddiskvolume";
        public const string DeviceMup = @"\device\mup";
        //public const string RemoteDeviceSlash = @"\msrc"; //XXXIS: CHANGE THIS BACK BEFORE RUNNING EXPERIMENTS ON MSRC CLUSTERS!
        public const string RemoteDeviceSlash = @"\ioan";
        public const string RemoteDeviceDoubleSlash = @"\\";
        private int NextFreeFlowId = 0;
        private ReaderWriterLockSlim LockIoFlow;
        private uint IocpSizeofOverlappedArray = 0;
        private BridgeOktofsAgent bridgeOktofsAgent = null;

        #region Win32 API
        const uint S_OK = 0;
        const uint WIN32_INFINITE = 0xffffffff;
        const uint WIN32_IOCP_TIMEOUT_ONE_MS = 1;
        const int WIN32_ERROR_GEN_FAILURE = 31;
        const int WIN32_WAIT_TIMEOUT = 258;
        const uint WIN32_ERROR_IO_PENDING = 997;
        const uint FLT_PORT_FLAG_SYNC_HANDLE = 0x00000001;    // See fltuser.h


        [DllImport("fltlibIoFlow.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern uint FilterConnectCommunicationPort(
            string lpPortName,           // LPCWSTR lpPortName,
            uint dwOptions,              // DWORD dwOptions,
            IntPtr lpContext,            // LPCVOID lpContext,
            uint dwSizeOfContext,        // WORD dwSizeOfContext
            IntPtr lpSecurityAttributes, // LP_SECURITY_ATTRIBUTES lpSecurityAttributes
            out SafeFileHandle hPort);  // HANDLE *hPort

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(SafeFileHandle hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateIoCompletionPort(
            SafeFileHandle FileHandle,
            IntPtr ExistingCompletionPort,
            IntPtr CompletionKey,
            uint NumberOfConcurrentThreads);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetQueuedCompletionStatus(
            IntPtr CompletionPort,
            out uint lpNumberOfBytes,
            out uint lpCompletionKey,
            out IntPtr lpOverlapped,
            uint dwMilliseconds);

        [DllImport("kernel32", SetLastError = true)]
        static extern bool CancelIo(
            SafeFileHandle hFile);              // handle to file

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetQueuedCompletionStatusEx(
            IntPtr CompletionPort,
            IntPtr lpCompletionPortEntries,
            uint ulCount,
            out uint ulNumEntriesRemoved,
            uint dwMilliseconds,
            bool fAlertable);


        #endregion

        #region constructor
        /// <summary>
        /// Preferred ctor for now.
        /// </summary>
        /// <param name="maxIocpThreads"></param>
        public IoFlowRuntime(uint maxIocpThreads)
        {
            IoFlowRuntimeInternal(maxIocpThreads,false);
        }

        /// <summary>
        /// Experimental ctor using GetQueuedCompletionStatusEx().
        /// </summary>
        /// <param name="maxIocpThreads"></param>
        /// <param name="iocpSizeofOverlappedArray"></param>
        public IoFlowRuntime(uint maxIocpThreads, uint iocpSizeofOverlappedArray)
        {
            IocpSizeofOverlappedArray = iocpSizeofOverlappedArray;
            IoFlowRuntimeInternal(maxIocpThreads,true);
        }

        private void IoFlowRuntimeInternal(uint maxIocpThreads, bool useIocpEx)
        {
            Console.WriteLine("IoFlowRuntime created with {0} iocp threads", maxIocpThreads);

            this.countIocpThreads = maxIocpThreads;

            LockIoFlow = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

            if (this.countIocpThreads < System.Environment.ProcessorCount)
                Console.WriteLine("IoFlowRuntime: Warning: MPL {0} < ProcessorCount {1}",
                    this.countIocpThreads, System.Environment.ProcessorCount);
            uint HResult;
            TenantId = (uint)Process.GetCurrentProcess().Id;

            //
            // Open FltMgr comms port for asynchronous data exchange with IoFlowUser minifilter driver.
            //
            string strDataPortName = Parameters.DATA_PORT_NAME;

            HResult = FilterConnectCommunicationPort(strDataPortName,      // LPCWSTR lpPortName,
                                                     0,                    // DWORD dwOptions,
                                                     IntPtr.Zero,          // LPCVOID lpContext,
                                                     0,                    // WORD dwSizeOfContext
                                                     IntPtr.Zero,          // LP_SECURITY_ATTRIBUTES lpSecurityAttributes
                                                     out hDataAsyncPort); // HANDLE *hPort

            if (HResult != S_OK)
            {
                Console.WriteLine("IoFlowRuntime failed to contact IoFlow minifilter driver async: check the driver is loaded.");
                Marshal.ThrowExceptionForHR((int)HResult);
            }

            //
            // Open FltMgr comms port for control messages to IoFlowUser minifilter driver.
            // We open the control port for synchronous I/O because we required calls to
            // FilterReplyMessage to complete synchronously when pending IRPs to kernel-mode
            // cancelsafe queue *before* the app calls CompletePendedPreOp() or
            // CompletePendedPostOp() in the days when we supported pending ops.
            //
            string strControlPortName = Parameters.CONTROL_PORT_NAME;

            HResult = FilterConnectCommunicationPort(strControlPortName, // LPCWSTR lpPortName,
                                                     0,                  // DWORD dwOptions,
                                                     IntPtr.Zero,        // LPCVOID lpContext,
                                                     0,                  // WORD dwSizeOfContext
                                                     IntPtr.Zero,        // LP_SECURITY_ATTRIBUTES lpSecurityAttributes
                                                     out hControlPort);  // HANDLE *hPort

            if (HResult != S_OK)
            {
                Marshal.ThrowExceptionForHR((int)HResult);
            }

            //
            // Purge any old state from the minifilter driver.
            //
            DriverReset();

            //
            // Dictionary implements lookup func f(FlowId)=>IoFlow
            //
            DictIoFlow = new Dictionary<uint, IoFlow>();

            //
            // Open I/O completion port for completing async I/O to the minifilter driver.
            //
            hCompletionPort = CreateIoCompletionPort(hDataAsyncPort,
                                                     hCompletionPort,         // HANDLE ExistingCompletionPort
                                                     IntPtr.Zero,             // ULONG_PTR CompletionKey
                                                     (uint)maxIocpThreads); // DWORD NumberOfConcurrentThreads
            if (hCompletionPort == IntPtr.Zero)
            {
                int err = Marshal.GetHRForLastWin32Error();
                Console.WriteLine("CreateIoCompletionPort err={0:X8}", err);
                Marshal.ThrowExceptionForHR(err);
            }

            //
            // Init dict for translating friendly drive names to unfriendly device names.
            // For example on my dev machine "D:" => "\Device\HarddiskVolume4"
            // No .NET support for discovering this, so we scan stdout from "fltmc volume".
            //
            DriveNameToVolumeName = new Dictionary<string, string>();
            Process process = new Process();
            process.StartInfo = new ProcessStartInfo("fltmc.exe", "volumes");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();
            string s1 = null;
            string[] separators = new string[] { " ", "\t" };
            while ((s1 = process.StandardOutput.ReadLine()) != null)
            {
                if (s1.Length > 1 && s1.Contains(@":"))
                {
                    string[] toks = s1.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                    if (toks.Length > 1 &&
                        toks[0].Length == 2 &&
                        toks[0].Substring(1, 1).Equals(@":") &&
                        toks[1].Length > DeviceHarddiskVolume.Length &&
                        toks[1].ToLower().StartsWith(DeviceHarddiskVolume))
                    {
                        DriveNameToVolumeName.Add(toks[0].ToLower(), toks[1].ToLower());
                    }
                }
            }
            process.WaitForExit();

            VolumeNameToDriveName = DriveNameToVolumeName.ToDictionary(x => x.Value, x => x.Key); //gets the reverse dictionary (this assumes no duplicate values)

            //
            // Allocate pool of statically allocated Irp objects for I/O to/from minifilter driver.
            //
            int countIrps = Parameters.IRP_POOL_SIZE * (int)maxIocpThreads;
            FindIrp = new Dictionary<IntPtr, IRP>(countIrps);
            irpArray = new IRP[countIrps];
            for (int i = 0; i < irpArray.Length; i++)
            {
                IRP irp = new IRP(this, hDataAsyncPort);
                FindIrp.Add(irp.addrOverlapped, irp);
                irpArray[i] = irp;
                irp.State = IrpState.Free;
                FreeIrp(irp);
            }

            //
            // Prep the requested number of IO completion worker threads.
            //
            arrayIocpThreads = new Thread[countIocpThreads];
            for (int i = 0; i < countIocpThreads; i++)
            {
                if (useIocpEx)
                    arrayIocpThreads[i] = new Thread(IocpThreadProcEx);
                else
                    arrayIocpThreads[i] = new Thread(IocpThreadProc);
            }

            //
            // Iff a local oktofsagent exists try for a connection for fast sid lookup.
            // Assumes oktofsagent is listening on its default TCP port.
            //
            bridgeOktofsAgent = new BridgeOktofsAgent();
            if (bridgeOktofsAgent.Connect() && bridgeOktofsAgent.SendMessageRegister())
                Console.WriteLine("Registered with local oktofsAgent for fast (account,SID) lookup.");
            else
                bridgeOktofsAgent = null;

        }

        public void Start()
        {
            Start(true);
        }


        public void Start(bool blockCaller)
        {
            //
            // Start the IO completion worker threads.
            //
            LastThreadTerminated = new ManualResetEvent(false);
            for (int i = 0; i < countIocpThreads; i++)
            {
                arrayIocpThreads[i].Start();
                CountIocpThreadsRunning++;
            }

            //
            // Block calling thread until all done.
            //
            if (blockCaller)
            {
                Console.WriteLine("IoFlow main thread blocked for duration (good).");
                LastThreadTerminated.WaitOne();
                //
                // Done.
                //
                Close();
                Console.WriteLine("IoFlow shut down complete.");
            }
        }

        #endregion

        #region IrpPool
        public void FreeIrp(IRP irp)
        {
            if (irp.State != IrpState.Free)
                throw new ApplicationException("FreeIrp() : Irp!=Free.");
            irp.State = IrpState.Pool;

            lock (IrpPoolLock)
            {
                Debug.Assert(irp.Next == null, "FreeIrp expected single irp, got multi-irp chain");
                irp.Next = FreeIrpPool;
                FreeIrpPool = irp;
                irp.IoFlow = null;
                countFreeIrpList++;
            }
        }

        //
        // Obtain one Irp object from free Irp list.
        //
        internal IRP AllocateIrp()
        {
            IRP irp = null;

            lock (IrpPoolLock)
            {
                if (FreeIrpPool != null)
                {
                    irp = FreeIrpPool;
                    FreeIrpPool = FreeIrpPool.Next;
                    countFreeIrpList--;
                }
            }

            if (irp == null) // Expected to fail, with some diags.
                throw new ApplicationException("Free Irp pool exhausted");

            irp.Next = irp.Prev = null;
            irp.State = IrpState.Free;
            return irp;
        }


        #endregion

        #region IocpThread

        /// <summary>
        /// Iocp completion thread using GetQueuedCompletionStatusEx().
        /// You would expect to get benefit from this over GetQueuedCompletionStatus() but in tests
        /// we've not seen that, at least not yet, but you can select it at ctor time hoping we
        /// can find a way to unlock its perf in future. 
        /// </summary>
        /// <param name="o"></param>
        private void IocpThreadProcEx(object o)
        {
            try
            {
                Console.WriteLine("IoFlowRuntime: using experimental GetQueuedCompletionStatusEx() thread.");
                //
                // IOCP thread init: start multiple concurrent READ IRPS to driver.
                // Note all I/O must originate on this thread for Win32 CancelIo to work.
                //
                for (int i = 0; i < Parameters.COUNT_IO_READ_MPL; i++)
                {
                    IRP irp = AllocateIrp();
                    irp.StartMiniFilterGetMessage();
                }

                ThreadPriority oldThreadPriority = Thread.CurrentThread.Priority;
                Thread.CurrentThread.Priority = ThreadPriority.Highest;

                //
                // Prep for GetQueuedCompletionStatusEx().
                //
                IntPtr lpOverlapped;
                uint lpNumberOfBytes;
                uint lpCompletionKey;
                OVERLAPPED_ENTRY[] ArrayOverlappedEntry = new OVERLAPPED_ENTRY[IocpSizeofOverlappedArray];
                GCHandle gcArrayOverlappedEntry = GCHandle.Alloc(ArrayOverlappedEntry, GCHandleType.Pinned);
                IntPtr addrArrayOverlappedEntry = gcArrayOverlappedEntry.AddrOfPinnedObject();
                uint NumEntriesRemoved = 1;
                FLT_PREOP_CALLBACK_STATUS PreOpResult = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_WITH_CALLBACK;
                FLT_POSTOP_CALLBACK_STATUS PostOpResult = FLT_POSTOP_CALLBACK_STATUS.FLT_POSTOP_FINISHED_PROCESSING;

                //
                // IOCP main loop: think -- {completePrior; flow.Callback(IRP); startNext}.
                //
                while (ShuttingDown == false) // IOCP main loop.
                {

                    //
                    // Block until IoCompletionPort indicates a) I/O completion or b) some signal.
                    //
                    if (GetQueuedCompletionStatusEx(hCompletionPort,
                                                    addrArrayOverlappedEntry,
                                                    IocpSizeofOverlappedArray,
                                                    out NumEntriesRemoved,
                                                    Parameters.IocpTimeoutInfinite, // Parameters.IocpTimeoutMSecs, //WIN32_INFINITE,
                                                    false) == false)
                    {
                        int HResult = Marshal.GetHRForLastWin32Error();
                        int LastWin32Error = Marshal.GetLastWin32Error();

                        //
                        // Expect to get here when Close() has been called.
                        //
                        const uint THE_HANDLE_IS_INVALID = 0x80070006;  // Not an error.
                        const uint ERROR_ABANDONED_WAIT_0 = 0x800702DF; // Not an error.
                        if ((uint)HResult == THE_HANDLE_IS_INVALID || (uint)HResult == ERROR_ABANDONED_WAIT_0)
                            break;

                        //
                        // In absence of I/O load, iocp timeout opt triggers polled queued I/O support.
                        //
                        if (LastWin32Error == WIN32_WAIT_TIMEOUT)
                            continue;

                        //
                        // Handle I/O failures. Non-fatal iff timeout or if media disconnected.
                        //
                        if (LastWin32Error == WIN32_ERROR_GEN_FAILURE)
                        {
                            Console.WriteLine("warning: ignored iocp tx status ERROR_GEN_FAILURE");
                            continue;
                        }

                        //
                        // Unexpected error.
                        //
                        Console.WriteLine("LastWin32Error {0} HResult {1:X8}", LastWin32Error, HResult);
                        Marshal.ThrowExceptionForHR((int)HResult);
                    }

                    //
                    // One iteration per entry in array returned from I/O completion port.
                    //
                    for (int i = 0; i < (int)NumEntriesRemoved; i++)
                    {
                        lpOverlapped = ArrayOverlappedEntry[i].addrOverlapped;
                        lpCompletionKey = (uint)ArrayOverlappedEntry[i].lpCompletionKey.ToInt32();

                        //
                        // Get from native addr of completing OVERLAPPED struct to the associated managed code IRP.
                        //
                        IRP irp = FindIrp[lpOverlapped];

                        irp.CompleteMiniFilterGetMessage();

                        //
                        // Find the IoFlow for this IRP and call the user's callback function.
                        //
                        LockIoFlow.EnterReadLock();
                        irp.IoFlow = DictIoFlow[irp.IoFlowHeader.FlowId];
                        LockIoFlow.ExitReadLock();

                        //
                        // Indicate any late errors for earlier IO indicated on this flow.
                        // These can happen if an error happens in the driver *after* the
                        // filterMgr messaging API has indicated that the reply message has
                        // been successfully delivered to the driver. In priciple the owning
                        // app should also know due to IO failure.
                        //
                        if ((irp.IoFlowHeader.Flags & (uint)HeaderFlags.HeaderFlagLateIoError) != 0)
                        {
                            string msg = "One or more errors reported for earlier IO on flowId ";
                            msg = string.Format("{0} {1} file {2}", msg, irp.IoFlow.FlowId, irp.IoFlow.FileName);
                            throw new ExceptionIoFlow(msg);
                        }

                        //
                        // Throw iff the kmode IRP's buffer is too large for our k2u buffers.
                        // In this case we see incomplete data so any ops on the data are invalid.
                        //
                        if (irp.DataLength > irp.MaxDataLength)
                        {
                            string msg = string.Format("Err irp.DataLength({0}) > k2u buffer size({1})",
                                                       irp.DataLength, irp.MaxDataLength);
                            throw new ExceptionIoFlow(msg);
                        }

                        //
                        // Upcall to user-supplied callback routine.
                        // Note the minifilter only sends IRPs for flows that have registered an appropriate callback routine. 
                        //
                        bool IsPreOp = irp.IsPreOp, IsPostOp = !IsPreOp;
                        switch (irp.MajorFunction)
                        {
                            case MajorFunction.IRP_MJ_CREATE:
                                {
                                    if (IsPreOp)
                                        PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreCreate(irp);
                                    else
                                        PostOpResult = (FLT_POSTOP_CALLBACK_STATUS)irp.IoFlow.PostCreate(irp);
                                    break;
                                }
                            case MajorFunction.IRP_MJ_READ:
                                {
                                    if (IsPreOp)
                                        PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreRead(irp);
                                    else
                                        PostOpResult = (FLT_POSTOP_CALLBACK_STATUS)irp.IoFlow.PostRead(irp);
                                    break;
                                }
                            case MajorFunction.IRP_MJ_WRITE:
                                {
                                    if (IsPreOp)
                                        PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreWrite(irp);
                                    else
                                        PostOpResult = (FLT_POSTOP_CALLBACK_STATUS)irp.IoFlow.PostWrite(irp);
                                    break;
                                }
                            case MajorFunction.IRP_MJ_CLEANUP:
                                {
                                    if (IsPreOp)
                                        PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreCleanup(irp);
                                    else
                                        PostOpResult = (FLT_POSTOP_CALLBACK_STATUS)irp.IoFlow.PostCleanup(irp);
                                    break;
                                }
                            case MajorFunction.IRP_MJ_LOCK_CONTROL:
                                {
                                    if (IsPreOp)
                                        PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreLockControl(irp);
                                    else
                                        throw new NotImplementedException(String.Format("irp.IoFlow.FlowId {0} PostLockControl callback not supported.",
                                                                          irp.IoFlow.FlowId));
                                    break;
                                }

                            default:
                                throw new ApplicationException("should never get here.");
                        }

                        irp.SetStateCallbackReturned();
                        irp.IoFlowHeader.FilterReplyHeader.Status = 0;
                        irp.IoFlowHeader.Resultcode = (irp.IsPreOp ? (uint)PreOpResult : (uint)PostOpResult);

#if UseReplyAndGetNext
                        //
                        // App is done with this IRP.
                        // Send reply to minifilter driver and restart next upcall on same IRP.
                        //
                        irp.MiniFilterReplyAndGetNext(hDataAsyncPort, hControlPort);
#else // UseReplyAndGetNext
                        //
                        // Send reply to our minifilter driver.
                        //
                        irp.MiniFilterReplyMessage(hDataAsyncPort, hControlPort);

                        //
                        // The IRP is no longer in use by app - we can restart next upcall request on same IRP.
                        //
                        irp.StartMiniFilterGetMessage();
#endif // UseReplyAndGetNext


                    } //for (int i = 0; i < (int)NumEntriesRemoved; i++)
                } // while (ShuttingDown == false) // IOCP main loop.

                //
                // Shut down.
                //
                CancelIo(hDataAsyncPort);
            }
            catch (ThreadInterruptedException CaughtException)
            {
                if (Interlocked.Decrement(ref CountIocpThreadsRunning) == 0)
                    LastThreadTerminated.Set();
                if (ShuttingDown)
                    return;
                throw new ThreadInterruptedException(null, CaughtException);
            }
            if (Interlocked.Decrement(ref CountIocpThreadsRunning) == 0)
                LastThreadTerminated.Set();
        }
        
        /// <summary>
        /// Iocp completion thread using GetQueuedCompletionStatus().
        /// </summary>
        /// <param name="o"></param>
        private void IocpThreadProc(object o)
        {
            //
            // Start the READ requests to minifilter driver.
            //
            for (int i = 0; i < Parameters.COUNT_IO_READ_MPL; i++)
            {
                IRP irp = AllocateIrp();
                irp.StartMiniFilterGetMessage();
            }

            while (!ShuttingDown)
            {
                uint lpNumberOfBytes;
                uint lpCompletionKey;
                IntPtr lpOverlapped;
                FLT_PREOP_CALLBACK_STATUS PreOpResult = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_WITH_CALLBACK;
                FLT_POSTOP_CALLBACK_STATUS PostOpResult = FLT_POSTOP_CALLBACK_STATUS.FLT_POSTOP_FINISHED_PROCESSING;


                //
                // http://msdn.microsoft.com/en-us/library/windows/desktop/aa364986(v=vs.85).aspx
                //
                if (!GetQueuedCompletionStatus(hCompletionPort,     // HANDLE CompletionPort
                                               out lpNumberOfBytes, // LPDWORD lpNumberOfBytes
                                               out lpCompletionKey, // PULONG_PTR lpCompletionKey
                                               out lpOverlapped,    // LPOVERLAPPED lpOverlapped
                                               WIN32_INFINITE))     // DWORD dwMilliseconds
                {
                    int HResult = Marshal.GetHRForLastWin32Error();

                    //
                    // Expect to get here when Close() has been called.
                    //
                    const uint THE_HANDLE_IS_INVALID = 0x80070006;  // Not an error.
                    const uint ERROR_ABANDONED_WAIT_0 = 0x800702DF; // Not an error.
                    if ( (uint)HResult == THE_HANDLE_IS_INVALID || (uint)HResult == ERROR_ABANDONED_WAIT_0)
                        break;

                    //
                    // Unexpected error.
                    //
                    int LastWin32Error = Marshal.GetLastWin32Error();
                    Console.WriteLine("LastWin32Error {0} HResult {1:X8}", LastWin32Error, HResult);
                    Marshal.ThrowExceptionForHR((int)HResult);
                }

                //
                // Get from native addr of completing OVERLAPPED struct to the associated managed code IRP.
                //
                IRP irp = FindIrp[lpOverlapped];

                irp.CompleteMiniFilterGetMessage();

                //
                // Find the IoFlow for this IRP and call the user's callback function.
                //
                LockIoFlow.EnterReadLock();
                irp.IoFlow = DictIoFlow[irp.IoFlowHeader.FlowId];
                LockIoFlow.ExitReadLock();

                //
                // Indicate any late errors for earlier IO indicated on this flow.
                // These can happen if an error happens in the driver *after* the
                // filterMgr messaging API has indicated that the reply message has
                // been successfully delivered to the driver. In priciple the owning
                // app should also know due to IO failure.
                //
                if ((irp.IoFlowHeader.Flags & (uint)HeaderFlags.HeaderFlagLateIoError) != 0)
                {
                    string msg = "One or more errors reported for earlier IO on flowId ";
                    msg = string.Format("{0} {1} file {2}", msg, irp.IoFlow.FlowId, irp.IoFlow.FileName);
                    throw new ExceptionIoFlow(msg);
                }
                
                //
                // Throw iff the kmode IRP's buffer is too large for our k2u buffers.
                // In this case we see incomplete data so any ops on the data are invalid.
                //
                if (irp.DataLength > irp.MaxDataLength)
                {
                    string msg = string.Format("Err irp.DataLength({0}) > k2u buffer size({1})",
                                               irp.DataLength, irp.MaxDataLength);
                    throw new ExceptionIoFlow(msg);
                }

                //
                // Upcall to user-supplied callback routine.
                // Note the minifilter only sends IRPs for flows that have registered an appropriate callback routine. 
                //
                bool IsPreOp = irp.IsPreOp, IsPostOp = !IsPreOp;
                switch (irp.MajorFunction)
                {
                    case MajorFunction.IRP_MJ_CREATE:
                        {
                            if (IsPreOp)
                                PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreCreate(irp);
                            else
                                PostOpResult = (FLT_POSTOP_CALLBACK_STATUS)irp.IoFlow.PostCreate(irp);
                            break;
                        }
                    case MajorFunction.IRP_MJ_READ:
                        {
                            if (IsPreOp)
                                PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreRead(irp);
                            else
                                PostOpResult = (FLT_POSTOP_CALLBACK_STATUS)irp.IoFlow.PostRead(irp);
                            break;
                        }
                    case MajorFunction.IRP_MJ_WRITE:
                        {
                            if (IsPreOp)
                                PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreWrite(irp);
                            else
                                PostOpResult = (FLT_POSTOP_CALLBACK_STATUS)irp.IoFlow.PostWrite(irp);
                            break;
                        }
                    case MajorFunction.IRP_MJ_CLEANUP:
                        {
                            if (IsPreOp)
                                PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreCleanup(irp);
                            else
                                PostOpResult = (FLT_POSTOP_CALLBACK_STATUS)irp.IoFlow.PostCleanup(irp);
                            break;
                        }
                    case MajorFunction.IRP_MJ_LOCK_CONTROL:
                        {
                            if (IsPreOp)
                                PreOpResult = (FLT_PREOP_CALLBACK_STATUS)irp.IoFlow.PreLockControl(irp);
                            else
                                throw new NotImplementedException(String.Format("irp.IoFlow.FlowId {0} PostLockControl callback not supported.",
                                                                  irp.IoFlow.FlowId));
                            break;
                        }

                    default:
                        throw new ApplicationException("should never get here.");
                }

                irp.SetStateCallbackReturned();
                irp.IoFlowHeader.FilterReplyHeader.Status = 0;
                irp.IoFlowHeader.Resultcode = (irp.IsPreOp ? (uint)PreOpResult : (uint)PostOpResult );

#if UseReplyAndGetNext
                //
                // App is done with this IRP.
                // Send reply to minifilter driver and restart next upcall on same IRP.
                //
                irp.MiniFilterReplyAndGetNext(hDataAsyncPort, hControlPort);
#else // UseReplyAndGetNext
                //
                // Send reply to our minifilter driver.
                //
                irp.MiniFilterReplyMessage(hDataAsyncPort, hControlPort);

                //
                // The IRP is no longer in use by app - we can restart next upcall request on same IRP.
                //
                irp.StartMiniFilterGetMessage();
#endif // UseReplyAndGetNext

            }

            if (Interlocked.Decrement(ref CountIocpThreadsRunning) == 0)
                LastThreadTerminated.Set();
        }
        #endregion

        #region API
        /// <summary>
        /// For apps that don't care about FlowIds. Note: may collide with any expicitly specified FlowIds.
        /// </summary>
        /// <returns></returns>
        public uint GetNextFreeFlowId()
        {
            return (uint)Interlocked.Increment(ref NextFreeFlowId);
        }

        /// <summary>
        /// Reset runtime state and driver state.
        /// </summary>
        /// <returns></returns>
        public uint RuntimeReset()
        {
            uint result = DriverReset();
            LockIoFlow.EnterWriteLock();
            DictIoFlow = new Dictionary<uint, IoFlow>();
            LockIoFlow.ExitWriteLock();
            return result;
        }

        /// <summary>
        /// Reset driver state: clears down all structs.
        /// </summary>
        /// <returns></returns>
        public uint DriverReset()
        {
            return Driver.Reset.Request(hControlPort);
        }

        /// <summary>
        /// Creates an IoFlow structure in runtime and corresponding structures (Flow and RAP)
        /// in the driver.
        /// </summary>
        /// <param name="accountName">A valid NT security account of form EUROPE\gregos.</param>
        /// <param name="fileName">A valid file name of form C:\tmp\gregos.txt or \Device\HarddiskVolumen\tmp\gregos.txt</param>
        /// <param name="callbackPreCreate">Callback func to invoke for IRPs on PreCreate path.
        /// Corresponds to an IFS Minifilter PRE_OPERATION callback ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff551109(v=vs.85).aspx</param>
        /// <param name="callbackPostCreate">Callback func to invoke for IRPs on PostCreate path.
        /// Corresponds to an IFS Minifilter POST_OPERATION callback ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff551107(v=vs.85).aspx</param>
        /// <param name="callbackPreRead">Callback func to invoke for IRPs on PreRead path.
        /// Corresponds to an IFS Minifilter PRE_OPERATION callback ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff551109(v=vs.85).aspx</param>
        /// <param name="callbackPostRead">Callback func to invoke for IRPs on PostRead path
        /// Corresponds to an IFS Minifilter POST_OPERATION callback ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff551107(v=vs.85).aspx</param>
        /// <param name="callbackPreWrite">Callback func to invoke for IRPs on PreWrite path.
        /// Corresponds to an IFS Minifilter PRE_OPERATION callback ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff551109(v=vs.85).aspx</param>
        /// <param name="callbackPostWrite">Callback func to invoke for IRPs on PostWrite path.
        /// Corresponds to an IFS Minifilter POST_OPERATION callback ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff551107(v=vs.85).aspx</param>
        /// <param name="callbackPreCleanup">Callback func to invoke for IRPs on PreCleanup path.
        /// Corresponds to an IFS Minifilter PRE_OPERATION callback ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff551109(v=vs.85).aspx</param>
        /// <param name="callbackPostCreate">Callback func to invoke for IRPs on PostCleanup path.
        /// Corresponds to an IFS Minifilter POST_OPERATION callback ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff551107(v=vs.85).aspx</param>
        /// <param name="context">Some object reference of your own for additional state associated with thie IoFlow</param>
        /// <returns></returns>
        public IoFlow CreateFlow(
            uint flowId,
            string accountName,
            string fileName,
            CallbackPreCreate callbackPreCreate,
            CallbackPostCreate callbackPostCreate,
            CallbackPreRead callbackPreRead,
            CallbackPostRead callbackPostRead,
            CallbackPreWrite callbackPreWrite,
            CallbackPostWrite callbackPostWrite,
            CallbackPreCleanup callbackPreCleanup,
            CallbackPostCleanup callbackPostCleanup,
            CallbackPreLockControl callbackPreLockControl,
            object context)
        {
            IoFlow ioFlow = null;

            //
            // Validate fileName, transforming drive letters to volume names.
            //
            fileName = fileName.ToLower();
            string origFilename = fileName;
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentNullException("fileName");
            else if (fileName.Length < 2)
                throw new ArgumentOutOfRangeException("fileName", "too short");

            else if (fileName.Substring(1, 1).Equals(@":"))
            {
                string driveName = fileName.Substring(0, 2);  // think "C:"
                string deviceName = "";
                if (DriveNameToVolumeName.TryGetValue(driveName, out deviceName))
                    fileName = deviceName + fileName.Substring(2);
            }
            if (fileName.ToLower().StartsWith(DeviceMup.ToLower()))
            {
                Debug.Assert(0 == 1); //XXXIS: don't use \device\mup in the paths in the config file   
            }
            else if (fileName.ToLower().StartsWith(RemoteDeviceSlash.ToLower()))
            {
                fileName = DeviceMup + fileName;
            }
            else if (fileName.ToLower().StartsWith(RemoteDeviceDoubleSlash.ToLower()))
            {
                fileName = DeviceMup + fileName.Substring(1);
            }
            else if (!fileName.ToLower().StartsWith(DeviceHarddiskVolume))
                throw new ArgumentException(@"must be format e.g. C: or \Device\HarddiskVolume", "fileName");

#if gregos // "XXXET: not used, just for testing" crashes on launch due to FileToBlock.dll not found
            // for lock drives, test physical offset code
            // XXXET: not used, just for testing
            if (fileName.Substring(1, 1).Equals(@":") ||
                origFilename.ToLower().StartsWith(DeviceHarddiskVolume))
            {
                if (origFilename.ToLower().StartsWith(DeviceHarddiskVolume))
                {
                    origFilename = getDriveLetterFileName(origFilename);
                }
                Int64 poffset = FileToBlock.GetPhysicalOffset(origFilename);
                Console.WriteLine("File {0} starts in physical offset {1}", origFilename, poffset);
            }
#endif
            //
            // Get SID for the given account name.
            //
            SecurityIdentifier sid = SidLookup(accountName);

            //
            // Create IoFlow object.
            //
            LockIoFlow.EnterWriteLock();
            try
            {
                ioFlow = new IoFlow(flowId, context, accountName, sid, fileName,
                                    callbackPreCreate, callbackPostCreate,
                                    callbackPreRead, callbackPostRead,
                                    callbackPreWrite, callbackPostWrite,
                                    callbackPreCleanup, callbackPostCleanup,
                                    callbackPreLockControl);
                DictIoFlow.Add(flowId, ioFlow);
                Driver.CreateFlow.Request(hControlPort, TenantId, flowId, ioFlow.Flags);
                Driver.CreateRap.Request(hControlPort, TenantId, flowId, accountName, sid, fileName);
            }
            catch (ExceptionIoFlow)
            {
                DriverReset();
                throw;
            }
            LockIoFlow.ExitWriteLock();

            return ioFlow;
        }

        private SecurityIdentifier SidLookup(string accountName)
        {
            SecurityIdentifier sid = null;

            if (bridgeOktofsAgent != null)
            {
                string stringSid = bridgeOktofsAgent.SendMessageSidQuery(accountName);
                if (stringSid != null)
                {
                    Console.WriteLine("SidLookup from oktofsagent cache ({0},{1})", accountName, stringSid);
                    sid = new SecurityIdentifier(stringSid);
                    return sid;
                }
            }

            //XXXET: following two lines are for account names of type europe\etheres
            try
            {
                NTAccount ntaccount = new NTAccount(accountName);
                sid = (SecurityIdentifier)ntaccount.Translate(typeof(SecurityIdentifier));
            }
            catch (Exception /*e*/)
            {
                // continue, it might be a VM name
            }
            if (sid == null)
            {
                try
                {
                    // try getting sid of VM
                    sid = (SecurityIdentifier)VmSID.GetVMnameSid(accountName);
                }
                catch (Exception /*e*/)
                {
                    // continue, it might be a SID string
                }
            }
            if (sid == null)
            {
                sid = new SecurityIdentifier(accountName);

            }
            if (sid == null)
                throw new Exception("Cannot find sid");

            return sid;
        }


        public string getDriveLetterFileName(string fileName)
        {
            string fName = null;

            if (String.Compare(fileName, 0, DeviceHarddiskVolume, 0, DeviceHarddiskVolume.Length, true) == 0) //for IRPs with file name like "\Device\Harddisk"
            {
                fName = fileName.Substring(DeviceHarddiskVolume.Length).Replace(@"\\", @"\");
                fName = fName.Substring(fName.IndexOf(@"\"));
                string volumeName = fileName.Substring(0, fileName.IndexOf(fName)).ToLower();

                return VolumeNameToDriveName[volumeName] + fName;
            }
            else if (String.CompareOrdinal(fileName, 0, DeviceMup, 0, DeviceMup.Length) == 0) //for IRPs with file name like "\Device\Mup"
            {
                return @"\" + fileName.Substring(DeviceMup.Length);
            }
            else
            {
                Debug.Assert(0 == 1); //We should only have IRPs talking to local drives, or over the network
            }

            return fName;
        }

        public string DriveLetterToDeviceName(string fileName)
        {
            Debug.Assert(fileName.Length > 2 && fileName.Substring(1,2).Equals(@":\"));
            string DriveLetter = fileName.Substring(0,2).ToLower();
            string tailName = fileName.Substring(2,fileName.Length - 2);
            if (!DriveNameToVolumeName.ContainsKey(DriveLetter))
                throw new ApplicationException(string.Format("Cannot find vol name for {0}",DriveLetter));
            return DriveNameToVolumeName[DriveLetter] + tailName;
        }

        #endregion

        #region close
        public void Close()
        {
            Dispose();
        }

        /// <summary>
        /// Ref documentation for IDisposable interface.
        /// Do not make this method virtual.
        /// A derived class should not be able to override this method.
        /// </summary>
        public void Dispose()
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
        void Dispose(bool calledByUserCode)
        {
            // Check to see if Dispose has already been called.
            if (!this.disposed)
            {
                if (calledByUserCode)
                {
                    //
                    // Dispose managed resources here.
                    //
                    if (bridgeOktofsAgent != null)
                        bridgeOktofsAgent.Close();
                    bridgeOktofsAgent = null;

                    // Start shutdown.
                    ShuttingDown = true;
                    if (hCompletionPort != IntPtr.Zero)
                    {
                        CloseHandle(hCompletionPort);
                        hCompletionPort = IntPtr.Zero;
                    }

                    Thread.Sleep(100);

                    for (int i = 0; i < irpArray.Length; i++)
                        irpArray[i].Close();

                }

                // 
                // Dispose unmanaged resources here.
                //
                if (hDataAsyncPort != null)
                    hDataAsyncPort.Close();
                hDataAsyncPort = null;
                if (hControlPort != null)
                    hControlPort.Close();
                hControlPort = null;
                if (hCompletionPort != IntPtr.Zero)
                    CloseHandle(hCompletionPort);
                hCompletionPort = IntPtr.Zero;


            }
            disposed = true;
        }

        // Use C# destructor syntax for finalization code.
        // This destructor will run only if the Dispose method 
        // does not get called.
        ~IoFlowRuntime()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        #endregion


    }
}
