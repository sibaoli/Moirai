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

    public class FILTER_REPLY_HEADER
    {
        public const int SIZEOF_FILTER_REPLY_HEADER = 16;
        private const int offsetStatus = 0;
        private const int offsetMessageId = 8;

        private byte[] buffer;

        public FILTER_REPLY_HEADER(byte[] buffer)
        {
            if (buffer.Length < SIZEOF_FILTER_REPLY_HEADER)
            {
                string err = string.Format("buffer.Length {0} smaller than {1}",
                    buffer.Length, SIZEOF_FILTER_REPLY_HEADER);
                throw new ArgumentOutOfRangeException(err);
            }
            this.buffer = buffer;
        }

        public uint Status
        {
            get
            {
                return (uint)Utils.Int32FromHostBytes(buffer, offsetStatus);
            }
            set
            {
                Utils.Int32ToHostBytes((int)value, buffer, offsetStatus);
            }
        }

    }
}
