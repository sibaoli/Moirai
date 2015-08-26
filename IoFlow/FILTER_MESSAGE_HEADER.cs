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
    public class FILTER_MESSAGE_HEADER
    {
        public const int SIZEOF_FILTER_MESSAGE_HEADER = 16;
        private const int offsetReplyLength = 0;
        private const int offsetMessageId = 8;

        private byte[] buffer;

        public FILTER_MESSAGE_HEADER(byte[] buffer)
        {
            if (buffer.Length < SIZEOF_FILTER_MESSAGE_HEADER)
            {
                string err = string.Format("buffer.Length {0} smaller than {1}",
                    buffer.Length, SIZEOF_FILTER_MESSAGE_HEADER);
                throw new ArgumentOutOfRangeException(err);
            }
            this.buffer = buffer;
        }

        public uint ReplyLength
        {
            get
            {
                return (uint)Utils.Int32FromHostBytes(buffer, offsetReplyLength);
            }
        }
    }
}
