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
using System.Runtime.Serialization;

namespace IoFlowNamespace
{
    [Serializable]
    public class ExceptionIoFlow : Exception
    {
        public ExceptionIoFlow() { }
        public ExceptionIoFlow(string s) : base(s) { }
        public ExceptionIoFlow(string s, Exception e) : base(s, e) { }
        protected ExceptionIoFlow(SerializationInfo si, StreamingContext sc)
            : base(si, sc) { }
    }

}
