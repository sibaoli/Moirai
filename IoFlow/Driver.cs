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
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;


namespace IoFlowNamespace
{
    internal class Driver
    {
        //
        // See ioflowuser.h for definitions of minifilter control codes.
        //
        private enum IOFLOWUSER_OPCODE
        {
            OpCodeInvalid,
            OpCodeQueryGlobalState,
            OpCodeCreateFlow,
            OpCodeQueryFlow,
            OpCodeUpdateFlow,
            OpCodeTenantDelete,
            OpCodeCreateRap,
            OpCodeQueryRap,
            OpCodeStatsZero,
            OpCodeReset,
            OpCodeReorg,
            OpCodeCommand,
            OpCodeQueryInstCtx,
            OpCodeInstCtxUpdate,
            OpCodeEndOfList
        };

        private enum RESULTCODES
        {
            OKTO_RESULT_SUCCESS = 0,
            OKTO_RESULT_EOF = 1,
            OKTO_RESULT_FLOW_NOT_FOUND = 2,
            OKTO_RESULT_RAP_NOT_FOUND = 3,
            OKTO_RESULT_OUT_OF_RESOURCES = 4,
            OKTO_RESULT_FLOW_ALREADY_EXISTS = 5,
            OKTO_RESULT_RAP_ALREADY_EXISTS = 6,
            OKTO_RESULT_INVALID_PARAMETER = 7,
            OKTO_RESULT_NMR_REGISTER_ERROR = 8,  // unused
            OKTO_RESULT_PACER_ERR_NEW_FLOW = 9,  // unused
            OKTO_RESULT_PACER_ERR_UPDATE_FLOW = 10,  // unused
            OKTO_RESULT_PACER_ERR_DELETE_FLOW = 11,  // unused
            OKTO_RESULT_INVALID_ADDRESS = 12,
            OKTO_RESULT_COUNT_RAP_NEQ_ZERO = 13,
            OKTO_RESULT_BUFFER_TOO_SMALL = 14,
            OKTO_RESULT_HASH_ADD_FAILURE = 15,
            OKTO_RESULT_TID_CANNOT_CHANGE = 16,   // Only generated in user mode.
            OKTO_RESULT_TID_NOT_AVAILABLE = 17,   // Only generated in user mode.
            OKTO_RESULT_INVALID_MESSAGE_LENGTH = 18,   // Only generated in user mode.
            OKTO_RESULT_INVALID_MESSAGE = 19,   // Only generated in user mode.
            OKTO_RESULT_IOCTL_ERROR = 20,   // Only generated in user mode.
            OKTO_RESULT_SOCK_ERROR = 21,   // Only generated in user mode.
            OKTO_RESULT_TENANT_NOT_FOUND = 22,   // Only generated in user mode.
            OKTO_RESULT_ALTER_STATS_SIZE = 23,
            OKTO_RESULT_EXCEEDS_STATS_SIZE = 24,
            OKTO_RESULT_INVALID_COMMAND = 25,
            OKTO_COMM_MSG_INCORRECT_SIZE = 26,
            OKTO_VM_NAME_INVALID_SID = 27,
            OKTO_VM_SID_TOO_LONG = 28,
            OKTO_VM_NAME_TOO_LONG = 29,
            OKTO_SHARE_NAME_TOO_LONG = 30,
            OKTO_RESULT_MESSAGE_NOT_SUPPORTED = 31,   // Only generated in user mode.
            OKTO_FLTMGR_ERROR = 32,
            OKTO_FLTMGR_INSTCTX_NOT_FOUND = 33,
            OKTO_FLUSH_IRPLISTS_FAILED = 34,
            OKTO_DOMAIN_ACCOUNTUSD_NAME_NO_USD = 35,
            OKTO_INVALID_PRIORITY = 36,
            OKTO_INVALID_WEIGHT = 37,
            OKTO_INSTU_INDEX_NOT_MONOTONIC = 38,
            OKTO_INSTU_INVALID_CAPACITY = 39,
            OKTO_INSTU_EXCEEDS_CAPACITY = 40,
            OKTO_INSTU_NOT_AT_H = 41,
            OKTO_IRP_NOT_ON_CANCELSAFE = 42,
            OKTO_RESULT_END_OF_RESULT_LIST = 43,   // Keep last in list: used as list terminator.
        }

        public static string DecodeOktoResult(uint ResultCode)
        {
            if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_SUCCESS) return "OKTO_RESULT_SUCCESS";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_EOF) return "OKTO_RESULT_EOF";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_FLOW_NOT_FOUND) return "OKTO_RESULT_FLOW_NOT_FOUND";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_RAP_NOT_FOUND) return "OKTO_RESULT_RAP_NOT_FOUND";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_OUT_OF_RESOURCES) return "OKTO_RESULT_OUT_OF_RESOURCES";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_FLOW_ALREADY_EXISTS) return "OKTO_RESULT_FLOW_ALREADY_EXISTS";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_RAP_ALREADY_EXISTS) return "OKTO_RESULT_RAP_ALREADY_EXISTS";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_INVALID_PARAMETER) return "OKTO_RESULT_INVALID_PARAMETER";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_NMR_REGISTER_ERROR) return "OKTO_RESULT_NMR_REGISTER_ERROR";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_PACER_ERR_NEW_FLOW) return "OKTO_RESULT_PACER_ERR_NEW_FLOW";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_PACER_ERR_UPDATE_FLOW) return "OKTO_RESULT_PACER_ERR_UPDATE_FLOW";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_PACER_ERR_DELETE_FLOW) return "OKTO_RESULT_PACER_ERR_DELETE_FLOW";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_INVALID_ADDRESS) return "OKTO_RESULT_INVALID_ADDRESS";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_COUNT_RAP_NEQ_ZERO) return "OKTO_RESULT_COUNT_RAP_NEQ_ZERO";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_BUFFER_TOO_SMALL) return "OKTO_RESULT_BUFFER_TOO_SMALL";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_HASH_ADD_FAILURE) return "OKTO_RESULT_HASH_ADD_FAILURE";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_TID_CANNOT_CHANGE) return "OKTO_RESULT_TID_CANNOT_CHANGE";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_TID_NOT_AVAILABLE) return "OKTO_RESULT_TID_NOT_AVAILABLE";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_INVALID_MESSAGE_LENGTH) return "OKTO_RESULT_INVALID_MESSAGE_LENGTH";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_INVALID_MESSAGE) return "OKTO_RESULT_INVALID_MESSAGE";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_IOCTL_ERROR) return "OKTO_RESULT_IOCTL_ERROR";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_SOCK_ERROR) return "OKTO_RESULT_SOCK_ERROR";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_TENANT_NOT_FOUND) return "OKTO_RESULT_TENANT_NOT_FOUND";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_ALTER_STATS_SIZE) return "OKTO_RESULT_ALTER_STATS_SIZE";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_EXCEEDS_STATS_SIZE) return "OKTO_RESULT_EXCEEDS_STATS_SIZE";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_INVALID_COMMAND) return "OKTO_RESULT_INVALID_COMMAND";
            else if (ResultCode == (uint)RESULTCODES.OKTO_COMM_MSG_INCORRECT_SIZE) return "OKTO_COMM_MSG_INCORRECT_SIZE";
            else if (ResultCode == (uint)RESULTCODES.OKTO_VM_NAME_INVALID_SID) return "OKTO_VM_NAME_INVALID_SID";
            else if (ResultCode == (uint)RESULTCODES.OKTO_VM_SID_TOO_LONG) return "OKTO_VM_SID_TOO_LONG";
            else if (ResultCode == (uint)RESULTCODES.OKTO_VM_NAME_TOO_LONG) return "OKTO_VM_NAME_TOO_LONG";
            else if (ResultCode == (uint)RESULTCODES.OKTO_SHARE_NAME_TOO_LONG) return "OKTO_SHARE_NAME_TOO_LONG";
            else if (ResultCode == (uint)RESULTCODES.OKTO_RESULT_MESSAGE_NOT_SUPPORTED) return "OKTO_RESULT_MESSAGE_NOT_SUPPORTED";
            else if (ResultCode == (uint)RESULTCODES.OKTO_FLTMGR_ERROR) return "OKTO_FLTMGR_ERROR";
            else if (ResultCode == (uint)RESULTCODES.OKTO_FLTMGR_INSTCTX_NOT_FOUND) return "OKTO_FLTMGR_INSTCTX_NOT_FOUND";
            else if (ResultCode == (uint)RESULTCODES.OKTO_FLUSH_IRPLISTS_FAILED) return "OKTO_FLUSH_IRPLISTS_FAILED";
            else if (ResultCode == (uint)RESULTCODES.OKTO_DOMAIN_ACCOUNTUSD_NAME_NO_USD) return "OKTO_DOMAIN_ACCOUNTUSD_NAME_NO_USD";
            else if (ResultCode == (uint)RESULTCODES.OKTO_INVALID_PRIORITY) return "OKTO_INVALID_PRIORITY";
            else if (ResultCode == (uint)RESULTCODES.OKTO_INVALID_WEIGHT) return "OKTO_INVALID_WEIGHT";
            else if (ResultCode == (uint)RESULTCODES.OKTO_INSTU_INDEX_NOT_MONOTONIC) return "OKTO_INSTU_INDEX_NOT_MONOTONIC";
            else if (ResultCode == (uint)RESULTCODES.OKTO_INSTU_INVALID_CAPACITY) return "OKTO_INSTU_INVALID_CAPACITY";
            else if (ResultCode == (uint)RESULTCODES.OKTO_INSTU_EXCEEDS_CAPACITY) return "OKTO_INSTU_EXCEEDS_CAPACITY";
            else if (ResultCode == (uint)RESULTCODES.OKTO_INSTU_NOT_AT_H) return "OKTO_INSTU_NOT_AT_H";
            else if (ResultCode == (uint)RESULTCODES.OKTO_IRP_NOT_ON_CANCELSAFE) return "OKTO_IRP_NOT_ON_CANCELSAFE";
            else return "OKTO_RESULT_UNDEFINED";
        }


        #region Win32 API
        const uint S_OK = 0;
        const uint WIN32_INFINITE = 0xffffffff;
        const uint WIN32_IOCP_TIMEOUT_ONE_MS = 1;
        const int WIN32_ERROR_GEN_FAILURE = 31;
        const int WIN32_WAIT_TIMEOUT = 258;
        const uint WIN32_ERROR_IO_PENDING = 997;

        [DllImport("fltlibIoFlow.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern uint FilterConnectCommunicationPort(
            string lpPortName,           // LPCWSTR lpPortName,
            uint dwOptions,              // DWORD dwOptions,
            IntPtr lpContext,            // LPCVOID lpContext,
            uint dwSizeOfContext,        // WORD dwSizeOfContext
            IntPtr lpSecurityAttributes, // LP_SECURITY_ATTRIBUTES lpSecurityAttributes
            out SafeFileHandle hPort);  // HANDLE *hPort

        [DllImport("fltlibIoFlow.dll", SetLastError = true  /*, CharSet = CharSet.Unicode */ )]
        static extern uint FilterSendMessage(
            SafeFileHandle hPort,      // HANDLE hPort,
            IntPtr lpInBuffer,         // LPVOID lpInBuffer,
            uint dwInBufferSize,       // DWORD dwInBufferSize,
            IntPtr lpOutBuffer,        // LPVOID lpOutBuffer,
            uint dwOutBufferSize,      // DWORD dwOutBufferSize,
            out uint lpBytesReturned); // LPDWORD lpBytesReturned

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);
        #endregion

        internal class Reset
        {
            private struct OKTOPUS_RESET_REQUEST
            {
                internal ulong OpCode;
            }

            private struct OKTOPUS_RESET_REPLY
            {
                internal uint Result;
            }

            public static uint Request(SafeFileHandle hPort)
            {
                uint HRESULT = 0;
                uint lpBytesReturned = 0;

                OKTOPUS_RESET_REQUEST Request = new OKTOPUS_RESET_REQUEST();
                OKTOPUS_RESET_REPLY Reply = new OKTOPUS_RESET_REPLY();

                Request.OpCode = (ulong)IOFLOWUSER_OPCODE.OpCodeReset;
                Reply.Result = 0;

                // Pin buffers for duration of synchronous call to FilterSendMessage.
                GCHandle gchRequest = GCHandle.Alloc(Request, GCHandleType.Pinned);
                GCHandle gchReply = GCHandle.Alloc(Reply, GCHandleType.Pinned);

                // FilterSendMessage() ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff541513(v=vs.85).aspx
                HRESULT = FilterSendMessage(hPort,                           // HANDLE hPort,
                                            gchRequest.AddrOfPinnedObject(), // LPVOID lpInBuffer,
                                            (uint)Marshal.SizeOf(Request),   // DWORD dwInBufferSize,
                                            gchReply.AddrOfPinnedObject(),   // LPVOID lpOutBuffer,
                                            (uint)Marshal.SizeOf(Reply),     // DWORD dwOutBufferSize,
                                            out lpBytesReturned);            // LPDWORD lpBytesReturned

                if (HRESULT != S_OK)
                {
                    Marshal.ThrowExceptionForHR((int)HRESULT);
                }

                gchRequest.Free();
                gchReply.Free();

                if (Reply.Result != (uint)RESULTCODES.OKTO_RESULT_SUCCESS)
                {
                    throw new ExceptionIoFlow(DecodeOktoResult(Reply.Result));
                }

                return HRESULT;
            }
        }

        internal class CreateFlow
        {
            private struct OKTOPUS_CREATE_FLOW_REQUEST
            {
                internal ulong OpCode;
                internal uint TenantId;
                internal uint FlowId;
                internal uint Flags;
            }

            private struct OKTOPUS_CREATE_FLOW_REPLY
            {
                internal uint Result;
            }

            public static uint Request(
                SafeFileHandle hPort,
                uint tenantId,
                uint flowId,
                uint flags)
            {
                uint HRESULT = 0;
                uint lpBytesReturned = 0;

                OKTOPUS_CREATE_FLOW_REQUEST Request = new OKTOPUS_CREATE_FLOW_REQUEST();
                OKTOPUS_CREATE_FLOW_REPLY Reply = new OKTOPUS_CREATE_FLOW_REPLY();

                Request.OpCode = (ulong)IOFLOWUSER_OPCODE.OpCodeCreateFlow;
                Request.TenantId = tenantId;
                Request.FlowId = flowId;
                Request.Flags = flags;
                Reply.Result = 0;

                // Pin buffers for duration of synchronous call to FilterSendMessage.
                GCHandle gchRequest = GCHandle.Alloc(Request, GCHandleType.Pinned);
                GCHandle gchReply = GCHandle.Alloc(Reply, GCHandleType.Pinned);

                // FilterSendMessage() ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff541513(v=vs.85).aspx
                HRESULT = FilterSendMessage(hPort,                           // HANDLE hPort,
                                            gchRequest.AddrOfPinnedObject(), // LPVOID lpInBuffer,
                                            (uint)Marshal.SizeOf(Request),   // DWORD dwInBufferSize,
                                            gchReply.AddrOfPinnedObject(),   // LPVOID lpOutBuffer,
                                            (uint)Marshal.SizeOf(Reply),     // DWORD dwOutBufferSize,
                                            out lpBytesReturned);            // LPDWORD lpBytesReturned

                if (HRESULT != S_OK)
                {
                    Marshal.ThrowExceptionForHR((int)HRESULT);
                }

                if (Reply.Result != (uint)RESULTCODES.OKTO_RESULT_SUCCESS)
                {
                    throw new ExceptionIoFlow(DecodeOktoResult(Reply.Result));
                }

                gchRequest.Free();
                gchReply.Free();

                return HRESULT;
            }
        }

        internal class CreateRap
        {
            const int OKTO_VM_NAME_BUFF_WCZ_MAX = 64;  // ref ioflowuser.h
            const int OKTO_VM_NAME_MAX_CHARS = (OKTO_VM_NAME_BUFF_WCZ_MAX - 1);    // Allow NULL terminator.
            const int OKTO_SHARE_NAME_BUFF_WCZ_MAX = 128; // ref ioflowuser.h
            const int OKTO_SHARE_NAME_MAX_CHARS = (OKTO_SHARE_NAME_BUFF_WCZ_MAX - 1); // Allow NULL terminator.
            const int OKTO_SID_BUFF_BYTE_LEN = 64; // ref ioflowuser.h

            const uint MAX_STATS_ARRAY_SIZE = 128;

            private unsafe struct OKTOPUS_CREATE_RAP_REQUEST
            {
                internal ulong OpCode;
                internal uint TenantId;           // Unique id assigned to each tenant.
                internal uint FlowId;             // Unique flow id within tenant, assigned by tenant
                internal uint MaxStatsArraySize;  // const max stats array size for this tenant.
                internal uint Result;             // Result of create request returned here.
                internal fixed byte VmNameBuff[OKTO_VM_NAME_BUFF_WCZ_MAX * 2];
                internal uint VmNameWCharLen;
                internal fixed byte ShareNameBuff[OKTO_SHARE_NAME_BUFF_WCZ_MAX * 2];
                internal uint ShareNameWCharZLen;
                internal uint SidLength;
                internal fixed byte Sid[OKTO_SID_BUFF_BYTE_LEN];
                internal uint PID;
                internal uint i_index;
                internal uint j_index;
            }

            private struct OKTOPUS_CREATE_RAP_REPLY
            {
                internal uint Result;
            }

            public unsafe static uint Request(
                SafeFileHandle hPort,
                uint tenantId,
                uint flowId,
                string accountName,
                SecurityIdentifier sid,
                string shareName)
            {
                uint HRESULT = 0;
                uint lpBytesReturned = 0;
                UnicodeEncoding enc = new UnicodeEncoding();
                OKTOPUS_CREATE_RAP_REQUEST Request = new OKTOPUS_CREATE_RAP_REQUEST();
                OKTOPUS_CREATE_RAP_REPLY Reply = new OKTOPUS_CREATE_RAP_REPLY();

                if (accountName.Length + 1 > OKTO_VM_NAME_MAX_CHARS)
                    throw new ExceptionIoFlow(DecodeOktoResult((uint)RESULTCODES.OKTO_VM_NAME_TOO_LONG));
                if (shareName.Length + 1 > OKTO_SHARE_NAME_MAX_CHARS)
                    throw new ExceptionIoFlow(DecodeOktoResult((uint)RESULTCODES.OKTO_SHARE_NAME_TOO_LONG));
                if (sid.BinaryLength > OKTO_SID_BUFF_BYTE_LEN)
                    throw new ExceptionIoFlow(DecodeOktoResult((uint)RESULTCODES.OKTO_VM_SID_TOO_LONG));

                //
                // Format request buffer.
                //
                Request.OpCode = (ulong)IOFLOWUSER_OPCODE.OpCodeCreateRap;
                Request.TenantId = tenantId;
                Request.FlowId = flowId;
                Request.MaxStatsArraySize = MAX_STATS_ARRAY_SIZE;
                Request.Result = 0;
                Request.VmNameWCharLen = ((uint)enc.GetByteCount(accountName) / 2) + 1;
                byte[] encVmNameBuff = new byte[OKTO_VM_NAME_BUFF_WCZ_MAX * 2];
                enc.GetBytes(accountName, 0, accountName.Length, encVmNameBuff, 0);
                for (int i = 0; i < OKTO_VM_NAME_BUFF_WCZ_MAX * 2; i++)
                    Request.VmNameBuff[i] = encVmNameBuff[i];
                Request.ShareNameWCharZLen = ((uint)enc.GetByteCount(shareName) / 2) + 1;
                byte[] encShareNameBuff = new byte[OKTO_SHARE_NAME_BUFF_WCZ_MAX * 2];
                enc.GetBytes(shareName, 0, shareName.Length, encShareNameBuff, 0);
                for (int i = 0; i < OKTO_SHARE_NAME_BUFF_WCZ_MAX * 2; i++)
                    Request.ShareNameBuff[i] = encShareNameBuff[i];
                Request.SidLength = (uint)sid.BinaryLength;
                byte[] sidBuff = new byte[sid.BinaryLength];
                sid.GetBinaryForm(sidBuff, 0);
                for (int i = 0; i < sid.BinaryLength; i++)
                    Request.Sid[i] = sidBuff[i];
                Request.PID = 0;
                Request.i_index = 0;
                Request.j_index = 0;


                Reply.Result = 0;

                // Pin buffers for duration of synchronous call to FilterSendMessage.
                GCHandle gchRequest = GCHandle.Alloc(Request, GCHandleType.Pinned);
                GCHandle gchReply = GCHandle.Alloc(Reply, GCHandleType.Pinned);

                // FilterSendMessage() ref http://msdn.microsoft.com/en-us/library/windows/hardware/ff541513(v=vs.85).aspx
                HRESULT = FilterSendMessage(hPort,                           // HANDLE hPort,
                                            gchRequest.AddrOfPinnedObject(), // LPVOID lpInBuffer,
                                            (uint)Marshal.SizeOf(Request),   // DWORD dwInBufferSize,
                                            gchReply.AddrOfPinnedObject(),   // LPVOID lpOutBuffer,
                                            (uint)Marshal.SizeOf(Reply),     // DWORD dwOutBufferSize,
                                            out lpBytesReturned);            // LPDWORD lpBytesReturned

                if (HRESULT != S_OK)
                {
                    Marshal.ThrowExceptionForHR((int)HRESULT);
                }

                if (Reply.Result != (uint)RESULTCODES.OKTO_RESULT_SUCCESS)
                {
                    throw new ExceptionIoFlow(DecodeOktoResult(Reply.Result));
                }

                gchRequest.Free();
                gchReply.Free();

                return HRESULT;
            }
        }

    }
}
