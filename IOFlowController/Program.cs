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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OktofsPolicyEssesNamespace;
using OktofsRateControllerNamespace;
 
namespace IOFlowController
{
    /// <summary>
    /// Simple exe front-end for launching an instance of an Oktofs Rate Controller.
    /// </summary>
    class Program
    {
        // Identify which type of Rate Controller is required. 
        enum Algorithms
        {
            Illegal = 0,
            esses = 1, // Moirai user-level cache controller
        }

        static void Main(string[] args)
        {
            Algorithms algorithm = Algorithms.Illegal;
            uint TenantId = 0;
            string configFileName = @"C:\Users\t-ioans\Documents\oktopus\oktopus\src\cs\IoFlow\IOFlowController\config-t-ioans-ioflow.txt";
            ulong Capacity = 1000000000000;
            int agentPort = Parameters.OKTOFSAGENT_TCP_PORT_NUMBER;
            double delta = 0.0;              // default no suppression of stats reports (was 5% delta).
            uint settleMillisecs = 0;
            uint sampleMillisecs = 1000;

            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];

                if (arg.ToLower().Equals("-a"))
                    algorithm = (Algorithms)Enum.Parse(typeof(Algorithms), args[++index]);
                else if (arg.ToLower().Equals("-t"))
                    TenantId = uint.Parse(args[++index]);
                else if (arg.ToLower().Equals("-f"))
                    configFileName = args[++index];
                else if (arg.ToLower().Equals("-c"))
                    Capacity = ulong.Parse(args[++index]);
                else if (arg.ToLower().Equals("-d"))
                    delta = double.Parse(args[++index]);
                else if (arg.ToLower().Equals("-p"))
                    agentPort = int.Parse(args[++index]);
                else if (arg.ToLower().Equals("-setl"))
                    settleMillisecs = uint.Parse(args[++index]);
                else if (arg.ToLower().Equals("-samp"))
                    sampleMillisecs = uint.Parse(args[++index]);
                else
                {
                    Console.WriteLine("Invalid argument: {0}", arg);
                    Usage();
                    return;
                }
            }

            try
            {
                if (algorithm != Algorithms.esses)
                {
                    Console.WriteLine("Algorithm not supported on oktofs.");
                }
                else if (TenantId == 0)
                {
                    Console.WriteLine("Invalid TenantId.");
                }
                else if (String.IsNullOrEmpty(configFileName) || String.IsNullOrWhiteSpace(configFileName))
                {
                    Console.WriteLine("Invalid configuration file name.");
                }
                else if (!File.Exists(configFileName))
                {
                    Console.WriteLine("Config file does not exist.");
                }
                else if (Capacity == 0)
                {
                    Console.WriteLine("Invalid BytesPerSec {0}.", Capacity);
                }
                else if (algorithm == Algorithms.esses)
                {
                    OktofsPolicyEsses Esses = new OktofsPolicyEsses(configFileName, "unused", agentPort);
                    Esses.Start(settleMillisecs, sampleMillisecs, delta);
                    Esses.Close();
                    Console.WriteLine("done.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Caught exception {0}", e.Message);
                throw;
            }
        }

        static void Usage()
        {
            Console.WriteLine(@"OktofsMaster -a s -t n -f s -bytesec n [-d n] [-p n] [-setl n] [-samp n] hostName.");
            Console.WriteLine("    -a s                 algorithm [constant|ascari].");
            Console.WriteLine("    -t n                 tenant id.");
            Console.WriteLine("    -f s                 config file name.");
            Console.WriteLine("    -c                   max capacity (R+W) in bytes per sec.");
            Console.WriteLine("    -d f                 delta (%age) for stats supression (deflt 0.0 = off).");
            Console.WriteLine("    -p n                 agent port number (default 6002).");
            Console.WriteLine("    -setl n              settle millisecs before stats sampling.");
            Console.WriteLine("    -samp n              stats sampling interval (millisecs).");
            Console.WriteLine("    -pri n               priority in range [0,7].");

        }

    }
}
