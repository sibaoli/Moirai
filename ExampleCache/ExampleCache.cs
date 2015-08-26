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
using System.Threading;
using System.IO;
using IoFlowNamespace;



namespace ExampleCacheNamespace
{
    public class ExampleCache
    {
        string inputConfig;
        int Mpl;
        Boolean noisyOutput = false;
        const int MaxFileSize = 10 * 1024 * 1024;  // 10MBytes.
        FileCache cache = null;
        ManualResetEvent CacheThreadBlocked = new ManualResetEvent(false);

        // constructor
        public ExampleCache(
            Boolean noisyOutputIn,
            string inputConfigIn,
            int mpl)
        {
            inputConfig = inputConfigIn;
            Mpl = mpl;
            noisyOutput = noisyOutputIn;
            // initialize cache
            cache = new FileCache(1024 /* bytes test */, noisyOutput);
        }

        public bool Start()
        {
            string thisMachine = System.Environment.MachineName.ToLower();

            //
            // Initialize IoFlow runtime.
            //
            IoFlowRuntime runtime = new IoFlowRuntime((uint)Mpl);

            string line;
            // get info from config file
            Debug.Assert(inputConfig != null && File.Exists(inputConfig));
            string[] separators = new string[] { " ", "\t" };
            System.IO.StreamReader f = new System.IO.StreamReader(inputConfig);

            // line has entries SRV_NAME, VM_NAME, VHD_NAME, cacheSizeMB, IO_req_size, write-back/write-through, noCacheWrites/cacheWrites
            while ((line = f.ReadLine()) != null)
            {
                string[] tokens = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;
                else if (tokens[0].StartsWith(@"#") || tokens[0].StartsWith(@"//"))
                    continue;
                if (tokens.Length < 7)
                {
                    throw new ApplicationException(String.Format("invalid config file"));
                }
                if (!(tokens[5].Equals("write-through") || tokens[5].Equals("write-back")))
                {
                    throw new ApplicationException(String.Format("invalid config file"));
                }
                if (!(tokens[6].Equals("cacheWrites") || tokens[6].Equals("noCacheWrites")))
                {
                    throw new ApplicationException(String.Format("invalid config file"));
                }

                Console.WriteLine("Got tokens {0}, {1}, {2}, {3}, {4}, {5}, {6}", tokens[0], tokens[1], tokens[2], tokens[3], tokens[4], tokens[5], tokens[6]);


                if (thisMachine.StartsWith(tokens[0].ToLower()))
                {
                    IoFlow f1 = runtime.CreateFlow(runtime.GetNextFreeFlowId(), tokens[1].ToLower(), tokens[2].ToLower(), PreCreate, null, PreRead, PostRead, PreWrite, PostWrite, PreCleanup, null, null, null);
                    cache.CacheSetFlowSize(f1.FlowId, Convert.ToUInt64(tokens[3]));
                   
                    //cache.CacheCreateGhostCache(f1.FlowId, Convert.ToUInt32(tokens[4])); //XXXIS: this should come from the controller...
                    cache.AlignedBlockSize = Convert.ToUInt32(tokens[4]);

                    switch (tokens[5])
                    {
                        case "write-through":
                            cache.SetCacheWritePolicy(CacheWritePolicy.WriteThrough);
                            break;
                    }

                    switch (tokens[6])
                    {
                        case "cacheWrites":
                            cache.SetCacheWriteBuffering(CacheWriteBuffer.Cache);
                            break;
                        case "noCacheWrites":
                            cache.SetCacheWriteBuffering(CacheWriteBuffer.noCache);
                            break;
                    }
                }

            }
            

            runtime.Start(false);

            CacheThreadBlocked.WaitOne();
            runtime.Close();
            return true;
        }

        private PreCreateReturnCode PreCreate(IRP irp)
        {
            return cache.CachePreCreate(irp);
            //return PreCreateReturnCode.FLT_PREOP_SUCCESS_NO_CALLBACK;
        }

        private PreReadReturnCode PreRead(IRP irp)
        {
            if (noisyOutput)
                Console.WriteLine("PreRead {0}: Got Offset={1} Length={2} Align={3}",
                    Thread.CurrentThread.ManagedThreadId, irp.FileOffset, irp.DataLength, cache.AlignedBlockSize);
            if (irp.DataLength % cache.AlignedBlockSize != 0 ||
                irp.FileOffset % cache.AlignedBlockSize != 0)
            {
                Console.WriteLine("PreRead {0}: Got UNALIGNED Offset={1} Length={2} Align={3}",
                    Thread.CurrentThread.ManagedThreadId, irp.FileOffset, irp.DataLength, cache.AlignedBlockSize);
            }
            //Console.WriteLine("OFFSETS,LENGTHS,{0},{1}", irp.FileOffset, irp.DataLength);
            //return PreReadReturnCode.FLT_PREOP_SUCCESS_NO_CALLBACK;
            return cache.CacheIRP(irp);
            //return PreReadReturnCode.FLT_PREOP_SUCCESS_NO_CALLBACK;
        }

        private PostReadReturnCode PostRead(IRP irp)
        {
            //return PostReadReturnCode.FLT_POSTOP_FINISHED_PROCESSING;
            return cache.CacheIRPCompleted(irp);
            //return PostReadReturnCode.FLT_POSTOP_FINISHED_PROCESSING;
        }
        private PreWriteReturnCode PreWrite(IRP irp)
        {
            if (noisyOutput)
                Console.WriteLine("PreWrite {0}: Got Offset={1} Length={2} Align={3}",
                    Thread.CurrentThread.ManagedThreadId, irp.FileOffset, irp.DataLength, cache.AlignedBlockSize);

            if (irp.DataLength % cache.AlignedBlockSize != 0 ||
                irp.FileOffset % cache.AlignedBlockSize != 0)
            {
                Console.WriteLine("PreWrite {0}: Got UNALIGNED Offset={1} Length={2} Align={3}",
                    Thread.CurrentThread.ManagedThreadId, irp.FileOffset, irp.DataLength, cache.AlignedBlockSize);
            }
            //Console.WriteLine("OFFSETS,LENGTHS,{0},{1}", irp.FileOffset, irp.DataLength);
            //return PreWriteReturnCode.FLT_PREOP_SUCCESS_NO_CALLBACK;
            return cache.CacheWriteIRP(irp);
            //return PreWriteReturnCode.FLT_PREOP_SUCCESS_NO_CALLBACK;
        }
        private PostWriteReturnCode PostWrite(IRP irp)
        {
            //return PostWriteReturnCode.FLT_POSTOP_FINISHED_PROCESSING;
            return cache.CacheWriteIRPCompleted(irp);
            //return PostWriteReturnCode.FLT_POSTOP_FINISHED_PROCESSING;
        }
        private PreCleanupReturnCode PreCleanup(IRP irp)
        {
            //return PreCleanupReturnCode.FLT_PREOP_SUCCESS_NO_CALLBACK;
            return cache.CachePreCleanup(irp);
        }
    }
}
