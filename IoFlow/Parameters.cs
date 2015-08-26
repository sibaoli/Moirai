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
    public static class Parameters
    {
        public static string DATA_PORT_NAME = @"\IoFlowDataPort";
        public static string CONTROL_PORT_NAME = @"\IoFlowControlPort";
        public const int IRP_POOL_SIZE = 128;                        // Statically allocated IRP pool.
        public const int COUNT_IO_READ_MPL = IRP_POOL_SIZE / 2;      // Number of READ IRP sent to minifilter.
        public const uint IOFLOW_K2U_DATA_BUFFER_SIZE = 1024 * 1024; // must match ioflow.h in driver
        internal const uint IocpTimeoutInfinite = 0xffffffff;        // Iocp times out after msecs iff no I/O.

    }
}
