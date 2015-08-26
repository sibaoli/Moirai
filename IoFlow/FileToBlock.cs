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
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace IoFlowNamespace
{
    public static class FileToBlock
    {
       [DllImport("FileToBlock.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto, SetLastError = true)]
       public static extern Int64 GetPhysicalOffsetStart(string driveName, string volumeHandleName, string stringfileName);
       
        /// <summary>
        /// Gets the physical LBA of the block device this file resides on.
        /// Expects filename of type <driveletter>:\filename, e.g., C:\test.txt
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Int64 GetPhysicalOffset(string filename)
        {
            string driveName = null;
            string volumeName = "\\\\.\\";
            if (filename.Substring(1, 1).Equals(@":"))
            {
                driveName = filename.Substring(0, 2);
                volumeName = volumeName + driveName;
                return GetPhysicalOffsetStart(driveName, volumeName, filename);
            }
            else
            {
                return -1;
            }
        }
    }
}
