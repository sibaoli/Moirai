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

namespace OktofsRateControllerNamespace
{
    public static class Parameters
    {
        public const int OKTO_VM_NAME_MAX_CHARS = 64;         // must match oktofsuser.h
        public const int OKTO_SHARE_NAME_MAX_CHARS = 64;      // must match oktofsuser.h
        public const int NETAGENT_TCP_PORT_NUMBER = 6000;     // same as oktofsagent.c default
        public const int OKTOFSAGENT_TCP_PORT_NUMBER = 6002;  // same as oktofsagent.c default
        public const int IOFLOWAGENT_TCP_PORT_NUMBER = 6003;  // same as in IoFlow network agent.
        public const int OKTO_SID_BUFF_BYTE_LEN = 64;         // must match oktofsuser.h
        public const int DEFAULT_MESSAGE_TIMEOUT_MS = 60000;  // Default msg timeout (fatal).
        public const int FLOWC_MESSAGE_TIMEOUT_MS = 120000;   // Message timeout (fatal).
        public const int RAPC_MESSAGE_TIMEOUT_MS = 120000;    // Message timeout (fatal).
        public const int SOCK_BUFFER_SIZE = 2 * 1024 * 1024;  // Conn sock buffs => max msg size.
        public const string TMP_DIR = @"C:\tmp";
        public const string LOG_DIR = TMP_DIR + @"\OktofsLogs";
        public const int OKTO_MAX_VEC_LEN = 8;                // Must match oktofsuser.h.
        public const string VOLNAME_ALIAS_REC_HEADER = "alias-sharename-to-volumename";


    }
}
