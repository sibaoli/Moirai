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
using IoFlowNamespace;
using System.Diagnostics;

namespace ExampleCacheNamespace
{
    //
    // Cache entry structure. One cache element.
    //
    public class FileCacheElement
    {
        public IoFlow Flow;        // flow this element belongs to
        public byte[] Data;        // data that is cached
        public UInt64 FileOffset;  // offset into file
        public uint DataLength;    // number of bytes cached
        public object LockObj;    // lock associated with this entry
        public bool Dirty;      // dirty bit for write-back cache policy
        public string fileName;     //XXXIS store file name with the drive letter for the write-back thread (fileName in Flow isn't good)
        public LinkedListNode<FileCacheElement> nodeInList; // location in LRU linked list
        public LinkedListNode<FileCacheElement> nodeInDirtyList; // location in Dirty linked list

        public LinkedListNode<FileCacheElement> nodeInFreeFileCacheList; // location in free file cache element linked list

        //
        // Constructor.
        // Copies InData locally
        public FileCacheElement(IoFlow InFlow, string InFileName, byte[] InData, UInt64 InFileOffset, uint copyOffset, uint InDataLength)
        {
            LockObj     = new object();
            fileName = InFileName;
            Flow        = InFlow;
            Data        = null;
            FileOffset  = InFileOffset;
            DataLength  = InDataLength;
            Dirty = false;
            nodeInList = null;
            nodeInDirtyList = null;
            nodeInFreeFileCacheList = null;
            if (InData != null)
            {
                Data = new byte[InDataLength];
                Buffer.BlockCopy(InData, (int)copyOffset, Data, 0, (int)InDataLength);
                //Array.Copy(InData, copyOffset, Data, 0, InDataLength);
            }
        }

        public void UpdateData(byte[] InData, uint copyOffset, uint InDataLength)
        {
            Debug.Assert(InDataLength == DataLength);
            if (InData != null)
            {
                if (Data == null)
                {
                    Data = new byte[InDataLength];
                }
                Buffer.BlockCopy(InData, (int)copyOffset, Data, 0, (int)InDataLength);

            }
        }
        public void UpdateNodeList(LinkedListNode<FileCacheElement> node)
        {
            nodeInList = node;
        }
        public void UpdateNodeDirtyList(LinkedListNode<FileCacheElement> node)
        {
            nodeInDirtyList = node;
        }
        public void UpdateNodeFreeFileCacheList(LinkedListNode<FileCacheElement> node)
        {
            nodeInFreeFileCacheList = node;
        }
    }
}
