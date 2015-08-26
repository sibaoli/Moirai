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
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace IoFlowNamespace
{
    //
    // Support for multiple concurrent async I/O operation with driver.
    //
    [StructLayout(LayoutKind.Sequential)]
    internal struct OVERLAPPED
    {
        private IntPtr Internal;
        private IntPtr InternalHigh;
        private uint Offset;
        private uint OffsetHigh;
        public IntPtr hEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OVERLAPPED_ENTRY
    {
        internal IntPtr lpCompletionKey;
        internal IntPtr addrOverlapped;
        private IntPtr Internal;
        internal uint dwNumberOfBytesTransferred;
    }

    internal enum IrpState
    {
        Illegal = 0,
        Pool = 1,
        Free = 2,
        GetMessageK2U = 3,
        CallbackRunning = 4,
        CallbackReturned = 5,
        ReplyMessageU2K = 6,
    }

    /// <summary>
    /// Managed code class implementing an IRP intercepted by the ioflowuser.sys minifilter driver.
    /// </summary>
    public class IRP
    {
        // 
        // The minifilter driver provides a byte[] array concatenating
        //     IOFLOW_HEADER   byte[SIZEOF_IOFLOW_HEADER]
        //     Data            byte[SIZEOF_MAX_IRP_DATA]
        // We expose the whole array to our caller asking them to avoid first SIZEOF_IOFLOW_HEADER bytes.
        // But we keep a hidden copy of the IOFLOW_HEADER just in case the user does corrupt those bytes.  
        //

        const uint SIZEOF_MAX_IRP_DATA = Parameters.IOFLOW_K2U_DATA_BUFFER_SIZE;    // ref IOFLOW_KERNEL_MESSAGE in ioflow.h
        const uint SIZEOF_IRP_USER_IO_BUFFER = IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER + SIZEOF_MAX_IRP_DATA;
        const uint SIZEOF_IRP_KERNEL_IO_BUFFER = 
            SIZEOF_IRP_USER_IO_BUFFER - FILTER_MESSAGE_HEADER.SIZEOF_FILTER_MESSAGE_HEADER;

        internal IRP Next = null;              // For constructing cheap lists of Irps.
        internal IRP Prev = null;              // For constructing cheap lists of Irps.
        internal byte[] Data = null;

        private GCHandle gcDataBuffer;         // GC handle used to pin DataBuffer memory for Win32 I/O.
        private ulong pDataBuffer;             // Win32 address of start of pinned DataBuffer memory.
        private IntPtr lpMessageBuffer;        // Native address of FILTER_MESSAGE_HEADER (start of DataBuffer). 
        internal IntPtr hEvent;                // Win32 event used in Win32 asynchronous I/O.
        private OVERLAPPED overlapped;         // Win32 OVERLAPPED struct, ref e.g. Windows SDK "WriteFile()".
        private GCHandle gcOVERLAPPED;         // GC handle pinning the OVERLAPPED struct.
        internal IntPtr addrOverlapped;        // Native address of the pinned OVERLAPPED struct.
        private IoFlowRuntime runtime = null;
        internal IrpState State = IrpState.Illegal;
        private SafeFileHandle hPort = null;
        private IOFLOW_HEADER ioFlowHeader = null;
        internal byte[] BackupHeaderBuffer = null;
        private IoFlow ioFlow = null;
        internal Boolean IsDataModified = false;
        private object LockState = new object();

        #region Win32
        const uint S_OK = 0;
        const int WIN32_ERROR_IO_PENDING = 997;
        const uint HRESULT_ERROR_IO_PENDING = 0x800703E5;  // Includes code "997"
                                                           // see http://msdn.microsoft.com/en-us/library/bb446131.aspx
                                                           // see http://msdn.microsoft.com/en-us/library/bb202810.aspx

        [DllImport("kernel32", SetLastError = true)]
        static extern IntPtr CreateEvent(
            IntPtr pAttributes,          // pointer to SECURITY_ATTRIBUTES
            bool bManualReset,           // is ResetEvent required
            bool bInitalSignalled,       // does it start signalled
            IntPtr pName );              // dont use as memory ownership is unclear

        [DllImport("kernel32", SetLastError = true)]
        static extern bool CloseHandle(
            IntPtr hObject);             // HANDLE hObject

        [DllImport("FltLibIoFlow.dll", SetLastError = true)]
        static extern uint FilterGetMessage(
            SafeFileHandle hPort,        // HANDLE hPort
            IntPtr lpMessageBuffer,      // PFILTER_MESSAGE_HEADER lpMessageBuffer
            uint   dwMessageBufferSize,  // DWORD dwMessageBufferSize
            IntPtr lpOverlapped  );      // LPOVERLAPPED lpOverlapped

        [DllImport("fltlibIoFlow.dll", SetLastError = true)]
        static extern uint FilterReplyMessage(
            SafeFileHandle hPort,        // HANDLE hPort
            IntPtr lpMessageBuffer,      // PFILTER_REPLY_HEADER lpReplyBuffer
            uint dwMessageBufferSize );  // DWORD dwReplyBufferSize

        [DllImport("fltlibIoFlow.dll", SetLastError = true)]
        static extern uint FilterReplyAndGetNext(
            SafeFileHandle hPort,        // HANDLE hPort
            IntPtr lpReplyBuffer,        // PFILTER_REPLY_HEADER lpReplyBuffer
            uint dwReplyBufferSize,      // DWORD dwReplyBufferSize
            IntPtr lpMessageBuffer,      // PFILTER_MESSAGE_HEADER lpMessageBuffer
            uint dwMessageBufferSize,    // DWORD dwReplyBufferSize
            IntPtr lpOverlapped);        // LPOVERLAPPED lpOverlapped

        #endregion

        public uint MaxDataLength { get { return SIZEOF_MAX_IRP_DATA; } }
        public IOFLOW_HEADER IoFlowHeader { get { return ioFlowHeader; } }
        public uint DataOffset { get { return IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER; } }
        public UInt64 FileOffset { get { return ioFlowHeader.FileOffset; } }
        public uint DataLength { get { return ioFlowHeader.DataLength; } }
        public bool IsPreOp { get { return ioFlowHeader.IsPreOp; } }
        public bool IsPostOp { get { return ioFlowHeader.IsPostOp; } }
        public uint IrpFlags { get { return ioFlowHeader.IrpFlags; } }
        public MajorFunction MajorFunction { get { return ioFlowHeader.MajorFunction; } }
        public byte MinorFunction { get { return ioFlowHeader.MinorFunction; } }
        public uint FlowId { get { return ioFlowHeader.FlowId; } }
        public uint Flags { get { return ioFlowHeader.Flags; } }
        public IoFlow IoFlow { get { return ioFlow; } set { ioFlow = value; } }
        public IntPtr IntPtrData { get { return lpMessageBuffer; } }
        public IoFlowRuntime IoFlowRuntime { get { return runtime; } }

        internal IRP(IoFlowRuntime ioFlow, SafeFileHandle hPort)
        {
            this.runtime = ioFlow;
            this.hPort = hPort;
            Data = new byte[SIZEOF_IRP_USER_IO_BUFFER];
            gcDataBuffer = GCHandle.Alloc(Data, GCHandleType.Pinned);
            pDataBuffer = (ulong)gcDataBuffer.AddrOfPinnedObject().ToInt64();
            lpMessageBuffer = gcDataBuffer.AddrOfPinnedObject();
            hEvent = CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);
            overlapped = new OVERLAPPED();
            gcOVERLAPPED = GCHandle.Alloc(overlapped, GCHandleType.Pinned);
            addrOverlapped = gcOVERLAPPED.AddrOfPinnedObject();
            overlapped.hEvent = hEvent;
            BackupHeaderBuffer = new byte[IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER];
            ioFlowHeader = new IOFLOW_HEADER(BackupHeaderBuffer);
        }

        internal void StartMiniFilterGetMessage()
        {
            if (State != IrpState.Free)
                throw new ApplicationException("StartMiniFilterGetMessage() Flags!=Free");
            State = IrpState.GetMessageK2U;

            uint HResult = FilterGetMessage(hPort,              // HANDLE hPort
                                            lpMessageBuffer,    // PFILTER_MESSAGE_HEADER lpMessageBuffer
                                            (uint)Data.Length,  // DWORD dwMessageBufferSize
                                            addrOverlapped);    // LPOVERLAPPED lpOverlapped
            if (HResult != HRESULT_ERROR_IO_PENDING)
            {
                int rc = Marshal.GetLastWin32Error();
                string msg = string.Format("StartMiniFilterGetMessage != ERROR_IO_PENDING {0} HRESULT {1:X8}", 
                                           rc.ToString(), HResult);
                Console.WriteLine(msg);
                Marshal.ThrowExceptionForHR((int)HResult);
            }
        }

        internal void CompleteMiniFilterGetMessage()
        {
            if (State != IrpState.GetMessageK2U)
            {
                string msg = string.Format("CompleteGetMsg() err Flags {0} expected RxRunning", State.ToString());
                throw new ApplicationException(msg);
            }

            //
            // Expose *copy* IOFLOW_HEADER to caller. This limits confusion when user
            // has damaged the header by writing below DataBuffer[SIZEOF_IOFLOW_HEADER].
            //
            Buffer.BlockCopy(Data, 0, BackupHeaderBuffer, 0, (int)IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER);

            State = IrpState.CallbackRunning;
            IsDataModified = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hDataPort"></param>
        internal void MiniFilterReplyMessage(SafeFileHandle hDataPort, SafeFileHandle hControlPort)
        {
            if (State != IrpState.CallbackReturned)
                throw new ApplicationException("MiniFilterReplyMessage() Flags!=ContainsIRP");

            State = IrpState.ReplyMessageU2K;

            ioFlowHeader.Flags |= (IsDataModified ? (uint)HeaderFlags.HeaderFlagIsDataModified : 0);

            //
            // Always restore the copy IOFLOW_HEADER, including FILTER_REPLY_HEADER.Status field. 
            //
            Buffer.BlockCopy(BackupHeaderBuffer, 0, Data, 0, (int)IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER);

            //
            // FltMgr reply message handler copies user reply buffer into a kernel-mode buffer.
            // The IoFlow driver needs to see the IOFLOW_HEADER, but only cause a buffer copy
            // of the data if the user modified the data.
            //
            uint replyDataLength = IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER;
            if (IsDataModified)
                replyDataLength += DataLength;

            uint HResult = FilterReplyMessage(hDataPort,         // HANDLE hPort - opened with 
                                              lpMessageBuffer,   // PFILTER_REPLY_HEADER lpReplyBuffer
                                              replyDataLength);  // valid data is at start of static buffer.

            if (HResult != S_OK)
            {
                int rc = Marshal.GetLastWin32Error();
                string msg = string.Format("MiniFilterReplyMessage != S_OK {0} hr 0x{1:X8}", 
                                           rc.ToString(), HResult);
                Console.WriteLine(msg);
                Marshal.ThrowExceptionForHR((int)HResult);
            }

            State = IrpState.Free;

        }

        /// <summary>
        /// Get access to IRP data READ mode: any mods made to user-mode data will be ignored
        /// by the ioflow driver. You can reverse the overwrite decision with a call to 
        /// GetDataReadWrite : last call wins.
        /// </summary>
        /// <returns></returns>
        public byte[] GetDataReadOnly()
        {
            IsDataModified = false;
            if (ioFlowHeader.IsPreOp && ioFlowHeader.MajorFunction == IoFlowNamespace.MajorFunction.IRP_MJ_READ)
                throw new ExceptionIoFlow("Data buffer contents undefined on PreOp(READ).");
            return Data;
        }

        /// <summary>
        /// Get access to IRP data in WRITE mode: the ioflow driver will overwrite kmode
        /// data with the user-mode data. You can reverse the overwrite decision with a
        /// call to GetDataReadOnly : last call wins.
        /// </summary>
        /// <returns></returns>
        public byte[] GetDataReadWrite()
        {
            IsDataModified = true;
            return Data;
        }

        internal void SetStateCallbackReturned()
        {
            State = IrpState.CallbackReturned;
        }

        /// <summary>
        /// Free native pinned mem for this Irp.
        /// </summary>
        public void Close()
        {
            if (hEvent != IntPtr.Zero && !CloseHandle(hEvent))
            {
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine("IRP.Close() err {0:X8}", err);
            }
            gcDataBuffer.Free();
            pDataBuffer = 0;
            lpMessageBuffer = IntPtr.Zero;
            gcOVERLAPPED.Free();
            addrOverlapped = IntPtr.Zero;
            Data = null;
        }


        internal void MiniFilterReplyAndGetNext(SafeFileHandle hDataPort, SafeFileHandle hControlPort)
        {
            if (State != IrpState.CallbackReturned)
                throw new ApplicationException("MiniFilterReplyMessage() Flags!=ContainsIRP");

            ioFlowHeader.Flags |= (IsDataModified ? (uint)HeaderFlags.HeaderFlagIsDataModified : 0);

            //
            // Always restore the copy IOFLOW_HEADER, including FILTER_REPLY_HEADER.Status field. 
            //
            Buffer.BlockCopy(BackupHeaderBuffer, 0, Data, 0, (int)IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER);

            //
            // FltMgr reply message handler copies user reply buffer into a kernel-mode buffer.
            // The IoFlow driver needs to see the IOFLOW_HEADER, but only cause a buffer copy
            // of the data if the user modified the data.
            //
            uint replyDataLength = IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER;
            if (IsDataModified)
                replyDataLength += DataLength;

            //
            // The call to CompleteMiniFilterGetMessage() is async and its completion handler
            // CompleteMiniFilterGetMessage() can get entered on another IOCP thread before 
            // the call to FilterReplyAndGetNext() has returned on this thread. Therefore
            // set the IRP state to a post FilterReplyAndGetNext() state *before* the call.
            //
            State = IrpState.GetMessageK2U;

            uint HResult = FilterReplyAndGetNext(hDataPort,
                                                 lpMessageBuffer,   // PFILTER_REPLY_HEADER lpReplyBuffer
                                                 replyDataLength,   // valid data is at start of static buffer.
                                                 lpMessageBuffer,   // PFILTER_MESSAGE_HEADER lpMessageBuffer
                                                 (uint)Data.Length, // DWORD dwMessageBufferSize
                                                 addrOverlapped);   // LPOVERLAPPED lpOverlapped

            if (HResult != HRESULT_ERROR_IO_PENDING)
            {
                int rc = Marshal.GetLastWin32Error();
                string msg = string.Format("FilterReplyAndGetNext != ERROR_IO_PENDING {0} HRESULT {1:X8}", 
                                           rc.ToString(), HResult);
                Console.WriteLine(msg);
                if (HResult == 0x80070057)
                    Console.WriteLine("Check that FltLibIoFlow.dll and ioflow.sys ioctl same build.");
                Marshal.ThrowExceptionForHR((int)HResult);
            }
        }

    }
}
