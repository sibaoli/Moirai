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

    /// <summary>
    /// An EndTag uniquely identifies a storage server. This is different from the EndPoint class
    /// in that the EndPoint class takes into account which client HyperV is connecting to the
    /// storage server, among other things. 
    /// </summary>
    public struct EndTag
    {
        public string share;    // Name of share (e.g., \Device\HarddiskVolume6)
        public string filter;   // Name of storage server (e.g., okto-009)
    }

    /// <summary>
    /// Lightweight convenience class for collecting together string names for a VM, share, server, etc. 
    /// </summary>
    public class Endpoint
    {

        readonly string sidOrAccount;
        readonly string shareOrVolume;
        public readonly string filterServer;   // Server on which this VM or service resides.
        public readonly string tag = null;     // Type of config file record that initialized this endpoint.
        public int index = -1;
        private string stringSid = null;
        readonly string hyperVserver;          // Hyper-V server where VM in question resides.
        private readonly uint key;
        private EndTag endId;
        private string stageIds;
        private readonly string fileName;

        public string FilterServer { get { return filterServer; } }
        public string SidOrAccount { get { return sidOrAccount; } }
        public string ShareOrVolume { get { return shareOrVolume; } }
        public string Tag { get { return tag; } }
        public int Index { get { return index; } }
        public string StringSid { get { return stringSid; } set { stringSid = value; } }
        public string HyperVserver { get { return hyperVserver; } }
        public uint Key { get { return key; } }
        public bool IsOktofsC { get { return stageIds.Contains("c"); } }
        public bool IsOktofsH { get { return stageIds.Contains("h"); } }
        public bool IsIoFlowD { get { return stageIds.Contains("d"); } }
        public string FileName { get { return fileName; } }

        public EndTag EndId { get { return endId; } }

        public Endpoint(
            string tag,
            string filterServer,
            string hyperVserver,
            string sidOrAccount,
            string shareOrVolume,
            uint key)
        {
            if (String.IsNullOrEmpty(sidOrAccount) || String.IsNullOrWhiteSpace(sidOrAccount))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg locName={0}", sidOrAccount));
            else if (String.IsNullOrEmpty(filterServer) || String.IsNullOrWhiteSpace(filterServer))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg serverName={0}", filterServer));
            else if (String.IsNullOrEmpty(tag) || String.IsNullOrWhiteSpace(tag))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg tag={0}", tag));
            else if (String.IsNullOrEmpty(tag) || String.IsNullOrWhiteSpace(shareOrVolume))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg shareOrVolume={0}", shareOrVolume));
            else if (String.IsNullOrEmpty(hyperVserver) || String.IsNullOrWhiteSpace(hyperVserver))
                throw new ArgumentOutOfRangeException(String.Format("Invalid arg hyperVhost={0}", hyperVserver));
            else
            {
                this.tag = tag.ToLower();
                this.filterServer = filterServer;
                this.hyperVserver = hyperVserver;
                this.sidOrAccount = sidOrAccount;
                this.shareOrVolume = shareOrVolume.ToLower();
                this.key = key;
                stageIds = tag.Substring(0, tag.IndexOf("-"));
                endId.filter = filterServer;
                endId.share = shareOrVolume;

                // If given a filename, extract from that the share name.
                if (this.tag.Contains("-file-"))
                {
                    fileName = this.shareOrVolume;
                    string[] toks = fileName.Split(new string[]{@"\"}, StringSplitOptions.RemoveEmptyEntries);
                    int lenShareName = 1 + toks[0].Length + 1 + toks[1].Length; // omit "\host\share".
                    this.shareOrVolume = this.shareOrVolume.Substring(0, lenShareName);
                }
            }
        }


        public void SetIndex(int index)
        {
            this.index = index;
        }
    }
}