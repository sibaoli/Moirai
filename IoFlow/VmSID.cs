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
using System.Management;
using System.Security.Principal;
namespace IoFlowNamespace
{
    public static class VmSID
    {
        public static SecurityIdentifier GetVMnameSid(string VMName)
        {
            ManagementObjectCollection queryCollection;
            SecurityIdentifier sid = null;
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\virtualization\v2");
                scope.Connect();
                string querystr = "SELECT * FROM Msvm_ComputerSystem where ElementName=\"" + VMName + "\"";
                ObjectQuery query = new ObjectQuery(querystr);

                ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
                queryCollection = searcher.Get();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return null;
            }
            //Console.WriteLine("Name,GUID,PID");

            try
            {
                foreach (ManagementObject vm in queryCollection)
                {
                    try
                    {
                        // display VM details
                        //Console.WriteLine("{0},{1},{2}", vm["ElementName"].ToString(),
                        //                    vm["Name"].ToString(), vm["ProcessID"].ToString());

                        string concat = "NT VIRTUAL MACHINE\\" + vm["Name"].ToString();
                        NTAccount ntaccount = new NTAccount(concat);
                        sid = (SecurityIdentifier)ntaccount.Translate(typeof(SecurityIdentifier));

                        Console.WriteLine("{0},{1},{2},{3}", vm["ElementName"].ToString(),
                                            vm["Name"].ToString(), vm["ProcessID"].ToString(), sid.ToString());
                       
                    }
                    catch (Exception /*e*/)
                    {
                        // don't print anything, some entries might miss fields like process id, ignore and move on
                        //Console.WriteLine(e.ToString());
                        continue;
                    }

                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return null;
            }
            return sid;
        }
    }
}
