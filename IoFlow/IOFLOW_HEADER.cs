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

namespace IoFlowNamespace
{
    // This must match the IOFLOW_HEADER_FLAGS enum in ioflow.h
    public enum HeaderFlags
    {
        HeaderFlagIsPreOp = 0x01,
        HeaderFlagIsPostOp = 0x02,
        HeaderFlagIsDataModified = 0x04,
        HeaderFlagIsIrpModified = 0x08,
        HeaderFlagCrShareRead = 0x10,
        HeaderFlagCrShareWrite = 0x20,
        HeaderFlagLateIoError = 0x40,
    }

    public struct CreateFlags
    {
        public bool FILE_SHARE_READ;
        public bool FILE_SHARE_WRITE;
        public CreateFlags(bool fileShareRead, bool fileShareWrite)
        {
            FILE_SHARE_READ = fileShareRead;
            FILE_SHARE_WRITE = fileShareWrite;
        }
    }

    public class IOFLOW_HEADER
    {
        public const uint SIZEOF_IOFLOW_HEADER = 144;    // in usermode only this includes fltmgr FILTER_MESSAGE_HEADER.
        public const int OffsetFilterMessageHeader = 0;
        public const int OffsetIopb = OffsetFilterMessageHeader + FILTER_MESSAGE_HEADER.SIZEOF_FILTER_MESSAGE_HEADER;
        public const int OffsetIoStatus = OffsetIopb + FLT_IO_PARAMETER_BLOCK.SIZEOF_FLT_IO_PARAMETER_BLOCK;
        public const int OffsetFlowId = OffsetIoStatus + IO_STATUS_BLOCK.SIZEOF_IO_STATUS_BLOCK;
        public const int OffsetFlags = OffsetFlowId + 4;
        public const int OffsetFileOffset = OffsetFlags + 4;
        public const int OffsetDataLength = OffsetFileOffset + 8;
        public const int OffsetResultcode = OffsetDataLength + 4;
        public const int OffsetIrpCtxIndex = OffsetResultcode + 4;
        public const int OffsetIrpCtxReuseCount = OffsetIrpCtxIndex + 4;
        public const int OffsetProcessID = OffsetIrpCtxReuseCount + 4;
        private readonly byte[] buffer;
        private readonly FILTER_MESSAGE_HEADER filterMessageHeader;
        private readonly FILTER_REPLY_HEADER   filterReplyHeader;
        private readonly FLT_IO_PARAMETER_BLOCK iopb;

        public FILTER_MESSAGE_HEADER FilterMessageHeader { get { return filterMessageHeader; } }
        public FILTER_REPLY_HEADER FilterReplyHeader { get { return filterReplyHeader; } }
        public FLT_IO_PARAMETER_BLOCK Iopb { get { return iopb; } }
        public bool IsPreOp { get { return ( (Flags & (uint)HeaderFlags.HeaderFlagIsPreOp) != 0 ? true : false); } }
        public bool IsPostOp { get { return !IsPreOp; } }
        public uint IrpFlags { get { return iopb.IrpFlags; } }
        public MajorFunction MajorFunction { get { return iopb.MajorFunction; } }
        public byte MinorFunction { get { return iopb.MinorFunction; } }
        public uint FlowId { get { return (uint)Utils.Int32FromHostBytes(buffer, OffsetFlowId); } }
        public uint Flags 
        {
            get { return (uint)Utils.Int32FromHostBytes(buffer, OffsetFlags); } 
            set { Utils.Int32ToHostBytes((int)value, buffer, OffsetFlags); } 
        }
        public UInt64 FileOffset { get { return (UInt64)Utils.Int64FromHostBytes(buffer, OffsetFileOffset); } }
        public uint DataLength { get { return (uint)Utils.Int32FromHostBytes(buffer, OffsetDataLength); } }
        public uint Resultcode
        {
            get { return (uint)Utils.Int32FromHostBytes(buffer, OffsetResultcode); }
            set { Utils.Int32ToHostBytes((int)value, buffer, OffsetResultcode); }
        }
        public uint IrpCtxIndex { get { return (uint)Utils.Int32FromHostBytes(buffer, OffsetIrpCtxIndex); } }
        public uint IrpCtxReuseCount { get { return (uint)Utils.Int32FromHostBytes(buffer, OffsetIrpCtxReuseCount); } }
        public UInt64 ProcessID { get { return (UInt64)Utils.Int64FromHostBytes(buffer, OffsetProcessID); } }
        public IOFLOW_HEADER(byte[] buffer)
        {
            if (buffer.Length < IOFLOW_HEADER.SIZEOF_IOFLOW_HEADER)
            {
                string err = string.Format("buffer.Length < SIZEOF_IRP_HEADER ({0})", SIZEOF_IOFLOW_HEADER);
                throw new ArgumentOutOfRangeException(err);
            }
            this.buffer = buffer;
            filterMessageHeader = new FILTER_MESSAGE_HEADER(buffer);
            filterReplyHeader = new FILTER_REPLY_HEADER(buffer);
            iopb = new FLT_IO_PARAMETER_BLOCK(buffer, OffsetIopb);
        }

        public CreateFlags GetCreateFlags()
        {
            if (this.MajorFunction != MajorFunction.IRP_MJ_CREATE)
                throw new ExceptionIoFlow("GetCreateFlags: IRP is not of type IRP_MJ_CREATE.");
            bool FILE_SHARE_READ = ((Flags & (uint)HeaderFlags.HeaderFlagCrShareRead) != 0 ? true : false);
            bool FILE_SHARE_WRITE = ((Flags & (uint)HeaderFlags.HeaderFlagCrShareWrite) != 0 ? true : false);
            return new CreateFlags(FILE_SHARE_READ, FILE_SHARE_WRITE);
        }

        public void SetCreateFlags(CreateFlags createFlags)
        {
            if (this.MajorFunction != MajorFunction.IRP_MJ_CREATE)
                throw new ExceptionIoFlow("SetCreateFlags: IRP is not of type IRP_MJ_CREATE.");
            if (this.IsPostOp)
                throw new ExceptionIoFlow("SetCreateFlags: invalid on PostOp callback.");

            bool wasRead = ((Flags & (uint)HeaderFlags.HeaderFlagCrShareRead) != 0 ? true : false);
            bool wasWrite = ((Flags & (uint)HeaderFlags.HeaderFlagCrShareWrite) != 0 ? true : false);
            if (wasRead != createFlags.FILE_SHARE_READ || wasWrite != createFlags.FILE_SHARE_WRITE)
            {
                if (wasRead && !createFlags.FILE_SHARE_READ)
                    Flags &= ~(uint)HeaderFlags.HeaderFlagCrShareRead;
                if (!wasRead && createFlags.FILE_SHARE_READ)
                    Flags |= (uint)HeaderFlags.HeaderFlagCrShareRead;
                if (wasWrite && !createFlags.FILE_SHARE_WRITE)
                    Flags &= ~(uint)HeaderFlags.HeaderFlagCrShareWrite;
                if (!wasWrite && createFlags.FILE_SHARE_WRITE)
                    Flags |= (uint)HeaderFlags.HeaderFlagCrShareWrite;
                Flags |= (uint)HeaderFlags.HeaderFlagIsIrpModified;
            }
        }

        public void DbgPrintOffsets()
        {
            // For cross-check against ioflowuser.sys DEBUGP()
            Console.WriteLine("IOFLOW_HEADER: SIZEOF_IOFLOW_HEADER = {0}", SIZEOF_IOFLOW_HEADER);
            Console.WriteLine("IOFLOW_HEADER: OffsetFilterMessageHeader = {0}", OffsetFilterMessageHeader);
            Console.WriteLine("IOFLOW_HEADER: OffsetIopb = {0}", OffsetIopb);
            Console.WriteLine("IOFLOW_HEADER: OffsetIoStatus = {0}", OffsetIoStatus);
            Console.WriteLine("IOFLOW_HEADER: OffsetFlowId = {0}", OffsetFlowId);
            Console.WriteLine("IOFLOW_HEADER: OffsetFlags = {0}", OffsetFlags);
            Console.WriteLine("IOFLOW_HEADER: OffsetOffset = {0}", OffsetFileOffset);
            Console.WriteLine("IOFLOW_HEADER: OffsetDataLength = {0}", OffsetDataLength);
            Console.WriteLine("IOFLOW_HEADER: OffsetResultcode = {0}", OffsetResultcode);
        }

    }

}
