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
    public class IO_STATUS_BLOCK
    {
        public const int SIZEOF_IO_STATUS_BLOCK = 16;
        public const int offStatus = 0;
        public const int offsetInformation = 8;  // ULONG_PTR (think UINT64).
    }
}
