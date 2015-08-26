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
    // see http://msdn.microsoft.com/en-us/library/windows/hardware/ff544638(v=vs.85).aspx
    public enum IrpFlags
    {
        IRP_BUFFERED_IO,            // The operation is a buffered I/O operation. 
        IRP_CLOSE_OPERATION,        // The operation is a cleanup or close operation. 
        IRP_DEALLOCATE_BUFFER,      // The I/O Manager will free the buffer during the completion phase for the IRP.
        IRP_INPUT_OPERATION,        // The operation is an input operation.
        IRP_NOCACHE,                // The operation is a noncached I/O operation.
        IRP_PAGING_IO,              // The operation is a paging I/O operation.
        IRP_SYNCHRONOUS_API,        // The I/O operation is synchronous.
        IRP_SYNCHRONOUS_PAGING_IO,  // The operation is a synchronous paging I/O operation.
        IRP_MOUNT_COMPLETION,       // A volume mount is completed for the operation. 
        IRP_CREATE_OPERATION,       // The operation is a create or open operation. 
        IRP_READ_OPERATION,         // The I/O operation is for reading.
        IRP_WRITE_OPERATION,        // The I/O operation is for writing.
        IRP_DEFER_IO_COMPLETION,    // I/O Completion of the operation is deferred.
        IRP_ASSOCIATED_IRP,         // The operation is associated with a master IRP.
        IRP_OB_QUERY_NAME,          // The operation is an asynchronous name query.
        IRP_HOLD_DEVICE_QUEUE,      // Reserved.
        IRP_UM_DRIVER_INITIATED_IO, // The operation originated from a user mode driver.
    }

    // see wdm.h
    public enum MajorFunction
    {
        IRP_MJ_CREATE                   = 0x00,
        IRP_MJ_CREATE_NAMED_PIPE        = 0x01,
        IRP_MJ_CLOSE                    = 0x02,
        IRP_MJ_READ                     = 0x03,
        IRP_MJ_WRITE                    = 0x04,
        IRP_MJ_QUERY_INFORMATION        = 0x05,
        IRP_MJ_SET_INFORMATION          = 0x06,
        IRP_MJ_QUERY_EA                 = 0x07,
        IRP_MJ_SET_EA                   = 0x08,
        IRP_MJ_FLUSH_BUFFERS            = 0x09,
        IRP_MJ_QUERY_VOLUME_INFORMATION = 0x0a,
        IRP_MJ_SET_VOLUME_INFORMATION   = 0x0b,
        IRP_MJ_DIRECTORY_CONTROL        = 0x0c,
        IRP_MJ_FILE_SYSTEM_CONTROL      = 0x0d,
        IRP_MJ_DEVICE_CONTROL           = 0x0e,
        IRP_MJ_INTERNAL_DEVICE_CONTROL  = 0x0f,
        IRP_MJ_SHUTDOWN                 = 0x10,
        IRP_MJ_LOCK_CONTROL             = 0x11,
        IRP_MJ_CLEANUP                  = 0x12,
        IRP_MJ_CREATE_MAILSLOT          = 0x13,
        IRP_MJ_QUERY_SECURITY           = 0x14,
        IRP_MJ_SET_SECURITY             = 0x15,
        IRP_MJ_POWER                    = 0x16,
        IRP_MJ_SYSTEM_CONTROL           = 0x17,
        IRP_MJ_DEVICE_CHANGE            = 0x18,
        IRP_MJ_QUERY_QUOTA              = 0x19,
        IRP_MJ_SET_QUOTA                = 0x1a,
        IRP_MJ_PNP                      = 0x1b,
    }


    public class FLT_IO_PARAMETER_BLOCK
    {
        public const int SIZEOF_FLT_IO_PARAMETER_BLOCK = 72;
        private readonly int OffsetIrpFlags;
        private readonly int OffsetMajorFunction;
        private readonly int OffsetMinorFunction;
        private readonly int OffsetOperationFlags;
        private readonly int OffsetReserved;

        private byte[] buffer;

        public FLT_IO_PARAMETER_BLOCK(byte[] buffer, int offset)
        {
            if (buffer.Length < SIZEOF_FLT_IO_PARAMETER_BLOCK)
            {
                string err = string.Format("buffer.Length {0} smaller than {1}",
                    buffer.Length, SIZEOF_FLT_IO_PARAMETER_BLOCK);
                throw new ArgumentOutOfRangeException(err);
            }
            this.buffer = buffer;
            OffsetIrpFlags = offset;
            OffsetMajorFunction = OffsetIrpFlags + 4;
            OffsetMinorFunction = OffsetMajorFunction + 1;
            OffsetOperationFlags = OffsetMinorFunction + 1;
            OffsetReserved = OffsetOperationFlags + 1;

        }

        public uint IrpFlags { get { return (uint)Utils.Int32FromHostBytes(buffer, OffsetIrpFlags); } }
        public MajorFunction MajorFunction { get { return (MajorFunction)buffer[OffsetMajorFunction]; } }
        public byte MinorFunction { get { return buffer[OffsetMinorFunction]; } }

    }
}
