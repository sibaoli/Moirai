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
using System.Diagnostics;
using System.Threading;
using System.IO;
using RateControllerNamespace;
using System.Net.Sockets;
using Microsoft.Win32;


namespace OktofsRateControllerNamespace
{
    // These must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
    public enum OktoAlertType
    {
        AlertUnused = 0x01,
        AlertDemoGui = 0x02,
        unused1 = 0x04,          // moved to AlertDemoGui -- available for reuse.
        unused2 = 0x08,          // moved to AlertDemoGui -- available for reuse.
        unused3 = 0x10,          // moved to AlertDemoGui -- available for reuse.
        unused4 = 0x20,          // moved to AlertDemoGui -- available for reuse.
        unused5 = 0x40,          // moved to AlertDemoGui -- available for reuse.
        AlertDebug = 0x80,       // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
        AlertTx = 0x100,         // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
        AlertRx = 0x200,         // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
        AlertIPv4TcpSyn = 0x400, // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
        AlertIPv4TcpFin = 0x800, // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
        AlertNoRap = 0x1000,     // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
        AlertEcn = 0x2000,       // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
        AlertNotActive = 0x4000, // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
        AlertRxStats = 0x8000,   // Must match the enum _OKTOPUS_INFO_EVENT in oktouser.h 
   
    }

    public enum AlertDemoGui
    {
        AlertUnused = 0x01,
        SetIsAlgorithmEnabled = 0x02,
        SetCapacity = 0x04,
        ChangePolicy = 0x08,
        ChangeLimitTokenSec = 0x10,
        ChangePriority = 0x20,
        SetControlInterval = 0x40,
        SetVecIdx = 0x80,            // tell policy mod which one of the n vec entries we want to show on GUI
    }


    public enum RateLimitType
    {
        LimitByteSec,
    }

    /// <summary>
    /// The RC state machines ensures that policy modules call our initialization and control
    /// routines in the correct order and at valid points in the control flow, and is also
    /// used to control when alerts are allowed to preempt our control loop.
    /// </summary>
    public enum RateControllerState
    {
        Init,           // All MessageCreateRap have been acked : expecting call to Start().
        SettleTimeout,  // Control loop: settle (dwell) interval delay.
        SettleCallback, // Settle delay expired : awaiting MessageStateZero acks.
        SampleTimeout,  // Control loop: stats sample interval delay.
        UpdateCallback, // Stats sample interval expired : awaiting MessageStateDelta acks.
        Fin,            // Shutdown : awaiting MessageTenantDelete acks.
    }

    public class OktofsRateController : INetPolicyModule, IConnectionCallback, IDisposable
    {
        /// <summary>
        /// Args to single work queue service routine: ensures sequential upcall into Policy Module. 
        /// </summary>
        internal enum RcWorkItemType
        {
            SettleTimeout,
            SampleTimeout,
            Alert,
        }
        internal class RcWorkItem
        {
            internal RcWorkItemType Type;
            internal long TimerUSecs;
            internal object Args;
            internal Connection connection;
            internal RcWorkItem(RcWorkItemType type, object args, long timerUSecs, Connection connection)
            {
                Type = type;
                Args = args;
                TimerUSecs = timerUSecs;
                this.connection = connection;
            }
        }

        IPolicyModule iPolicyModule;                                 // Interface to Policy Module. 
        int AgentPort = 6000;                                        // TCP port on which network agents listen.
        uint TenantId = 1;
        readonly Dictionary<string, Connection> AgentNameToConn;
        readonly Dictionary<string, Connection> IoFlowNameToConn;
        bool IsMatrixGenerated = false;
        List<RAP> ListRap;
        List<Flow> ListFlows = null;
        public SortedDictionary<string, int> ServerNameToIndex;      // Unique index number per Hyper-V server.
        public Dictionary<int, string> ServerIndexToName;            // Unique index number per Hyper-V server.
        readonly public Dictionary<string, string> DictVmNameToSid;  // Known (Server,VmName)->Sid mapping.
        public Dictionary<string, Device> DictDevice;                // Keyed on e.g. "\\HostName\Device\Mup".
        uint SeqNo = 0;                                              // Global SN id per (request,reply) msg pair.
        private object[] LockPendingReplies;                         // Parallel msg lock per reply msg type.
        private int[] CountPendingReplies = null;                    // Count pending replies for each msg type.
        private Dictionary<uint, MessageTypes> DictPendingReplies;   // Dict of pending replies keyed on SN.
        private int matrixDimension = 0;
        public delegate int FuncPtrSerialize(byte[] buffer, int offset);
        public bool trace = false;
        private object LockTimer = new object();
        private uint NextFreeFlowId = 1;
        private uint NextEndpointKey = 1;
        private Dictionary<uint, RAP> DictEpKeyToRap = new Dictionary<uint, RAP>();
        private UInt64 AlertVec = 0;
        private RateControllerState state = RateControllerState.Init;
        private object LockState = new object();                     // Concurrent threads access state variable.
        ManualResetEvent EventPolicyModuleThreadBlocked = new ManualResetEvent(false);
        internal WorkQueue<RcWorkItem> RcWorkQueue = null;
        SoftTimer softTimer = null;                                  // Provides per-timeout context.
        long CurrentTimeoutUSecs = 0;                                // Ignore timeouts overtaken by alerts.
        private uint settleMillisecs = 1000;
        private uint sampleMillisecs = 1000;
        private bool Shutdown = false;
        StreamWriter logStream = null;
        RateController netRateController = null;
        TrafficMatrixEntry[,] TrafficMatrix;   // Traffic matrix ordered by (loc,rem) addr pairs.
        List<TrafficMatrixEntry> AllVMs = null;       // Set of all VMs.
        List<TrafficMatrixEntry> AllServices = null;  // Set of all services.
        private Qpc qpc = null;
        private Dictionary<string, string> DictShareNameToVolumeName = null; // Stable vol names : NT alters them.
        private object LockLog = new object();

        #region public properties
        public bool Trace
        {
            get { return trace; }
            set { trace = value; }
        }

        public RateControllerState State
        {
            get { lock (LockState) { return state; } }
            set { lock (LockState) { state = value; }; }
        }

        public uint SettleMillisecs
        {
            get { return settleMillisecs; }
            set { settleMillisecs = value; }
        }

        public uint SampleMillisecs
        {
            get { return sampleMillisecs; }
            set { sampleMillisecs = value; }
        }
        #endregion

        /// <summary>
        /// The primary class of the Rate Controller implementation. Provides the 
        /// runtime environment for a Policy Module implementing a control algorithm.
        /// </summary>
        /// <param name="client">Reference to client policy module.</param>
        /// <param name="tenantId">Unique TenantId.</param>
        /// <param name="agentPort">Port on which network agents are listening.</param>
        public OktofsRateController(IPolicyModule client, uint tenantId, int agentPort)
        {
            //
            // Initialize state for Oktofs rate controller.
            //
            iPolicyModule = client;
            AgentPort = agentPort;
            TenantId = tenantId;
            AgentNameToConn = new Dictionary<string, Connection>(StringComparer.OrdinalIgnoreCase);
            IoFlowNameToConn = new Dictionary<string, Connection>(StringComparer.OrdinalIgnoreCase);
            LockPendingReplies = new object[(int)MessageTypes.EndOfList];
            for (int i = 0; i < LockPendingReplies.Length; i++)
                LockPendingReplies[i] = new object();
            CountPendingReplies = new int[(int)MessageTypes.EndOfList];
            DictPendingReplies = new Dictionary<uint, MessageTypes>();
            DictVmNameToSid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DictDevice = new Dictionary<string, Device>();
            const int TIMER_QUEUE_LENGTH = 16;
            softTimer = new SoftTimer(TIMER_QUEUE_LENGTH, this);
            qpc = new Qpc();

            //
            // Initialize a Oktopus network rate controller as an embedded object. 
            //
            netRateController = new RateController(this, tenantId, Parameters.NETAGENT_TCP_PORT_NUMBER);

            //
            // Callbacks into Policy Module are performed sequentially on a single work queue thread.
            // Work items on this queue are generated by Timeouts and by Alerts from network agents.
            //
            const int WORK_QUEUE_LENGTH = 128;
            const int WORK_QUEUE_MAX_READERS = 1;
            RcWorkQueue = new WorkQueue<RcWorkItem>(this.RunRcWorkQueueItem, WORK_QUEUE_MAX_READERS, WORK_QUEUE_LENGTH);
        }

        #region initialization

        private bool IsValidTag(string tag, string[] validTags)
        {
            foreach (string t in validTags)
                if (tag.ToLower().Equals(t.ToLower()))
                    return true;
            return false;
        }

        private string GetHostNameFromShareName(string shareName)
        {
            if (string.IsNullOrEmpty(shareName) ||
                string.IsNullOrWhiteSpace(shareName) ||
                shareName.Length < 3 ||
                (!shareName.Substring(0, 1).Equals(@"\") &&
                !shareName.Substring(0, 2).Equals(@"\\")) ||
                shareName.Substring(2).IndexOf(@"\") < 1)
            {
                throw new ApplicationException(String.Format("Failed to get hostname from shareName {0}", shareName));
            }
            string hostName = null;
            int idxEndHostName = -1;
            if (shareName.Substring(0, 2).Equals(@"\\"))
            {
                hostName = shareName.Substring(2);
                idxEndHostName = hostName.Substring(2).IndexOf(@"\");
            }
            else if (shareName.Substring(0, 1).Equals(@"\"))
            {
                hostName = shareName.Substring(1);
                idxEndHostName = hostName.Substring(1).IndexOf(@"\");
            }
            hostName = hostName.Substring(0, idxEndHostName + 1);
            return hostName;
        }

        /// <summary>
        /// Parses config file and returns list of Enpoints.
        /// </summary>
        /// <param name="fileName">Name of input file.</param>
        /// <param name="validTags">Acceptable input record types.</param>
        /// <returns>List containing one Endpoint for each input record.</returns>
        public List<Endpoint> ParseConfigFile(string fileName, string[] validTags)
        {
            List<Endpoint> listEndpoints = null;
            StreamReader streamIn = new StreamReader(fileName);
            string inputRecord;
            string[] separators = new string[] { " ", "\t" };

            ValidateState(RateControllerState.Init, "ParseConfigFile");
            while ((inputRecord = streamIn.ReadLine()) != null)
            {
                string[] inputTokens = inputRecord.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (inputTokens.Length == 0)
                    continue;
                else if (inputTokens[0].StartsWith(@"#") || inputTokens[0].StartsWith(@"//"))
                    continue;
                else if (inputTokens[0].ToLower().Equals(Parameters.VOLNAME_ALIAS_REC_HEADER))
                    continue;
                else if (inputTokens.Length > 4 &&
                    IsValidTag(inputTokens[0], validTags) &&
                    !inputTokens[0].StartsWith("-") &&
                    (inputTokens[0].ToLower().Contains("-vm-share-vol") ||
                     inputTokens[0].ToLower().Contains("-vm-file-vol")))
                {
                    //
                    // Parse records.
                    // Each such record generates a Flow containing one or more queue : e.g. one at C and one at H. 
                    //
                    string tag = inputTokens[0].ToLower();
                    string StageIds = tag.Substring(0, tag.IndexOf("-"));
                    bool isOktofsC = StageIds.Contains("c");
                    bool isOktofsH = StageIds.Contains("h");
                    bool isIoFlowD = StageIds.Contains("d");

                    Endpoint endpointC = null;
                    Endpoint endpointH = null;
                    if (listEndpoints == null)
                        listEndpoints = new List<Endpoint>();
                    string vmName = inputTokens[1];
                    string hypervName = inputTokens[2];
                    string shareName = inputTokens[3];
                    string volName = inputTokens[4];
                    // Windows keeps breaking our conf files by changing the volume names at H.
                    // Provide an optional alias func based on share name to avoid admin chaos.
                    volName = GetVolumeNameFromShareName(shareName, volName);

                    if (!StageIds.Replace("c", "").Replace("h", "").Replace("d", "").Equals(""))
                    {
                        streamIn.Close();
                        streamIn = null;
                        throw new ApplicationException(String.Format("input rec illegal prefix {0}", tag));
                    }
                    if (vmName.Length > Parameters.OKTO_VM_NAME_MAX_CHARS)
                    {
                        streamIn.Close();
                        streamIn = null;
                        throw new ApplicationException(String.Format("VmName too long {0}", vmName));
                    }
                    if (shareName.Length > Parameters.OKTO_SHARE_NAME_MAX_CHARS)
                    {
                        streamIn.Close();
                        streamIn = null;
                        throw new ApplicationException(String.Format("ShareName too long {0}", shareName));
                    }
                    if (volName.Length > Parameters.OKTO_SHARE_NAME_MAX_CHARS)
                    {
                        streamIn.Close();
                        streamIn = null;
                        throw new ApplicationException(String.Format("VolumeName too long {0}", volName));
                    }

                    string storageServer = GetHostNameFromShareName(shareName);
                    const int LastHeaderIndex = 4; // Index of last std header field in "ch-vm-share-vol" record.

                    if (isOktofsC || isIoFlowD)
                    {
                        endpointC = new Endpoint(tag, hypervName, hypervName, vmName, shareName, NextEndpointKey++);
                        listEndpoints.Add(endpointC);
                    }
                    if (isOktofsH)
                    {
                        endpointH = new Endpoint(tag, storageServer, hypervName, vmName, volName, NextEndpointKey++);
                        listEndpoints.Add(endpointH);
                    }
                    Flow flow = new Flow(endpointC, endpointH, inputRecord, inputTokens, LastHeaderIndex, this);
                    if (ListFlows == null)
                        ListFlows = new List<Flow>();
                    ListFlows.Add(flow);

                }
                else if (IsValidTag(inputTokens[0], validTags))
                {
                    // Silently ignore tags that we don't understand if caller said they are valid.
                }
                else
                {
                    string msg = String.Format("Illegal config file line: {0}", inputRecord);
                    streamIn.Close();
                    streamIn = null;
                    throw new ApplicationException(msg);
                }
            }
            streamIn.Close();
            //
            // Plumb index values into ordered list of Endpoints.
            //
            if (listEndpoints != null)
                for (int i = 0; i < listEndpoints.Count; i++)
                    listEndpoints[i].SetIndex(i);

            return listEndpoints;
        }

        public static Endpoint[] ArrayEndpoints(SortedList<UInt64, Endpoint> listEndpoints)
        {
            Endpoint[] arrayEndpoints = new Endpoint[listEndpoints.Count];
            int i = 0;
            foreach (Endpoint endpoint in listEndpoints.Values)
                arrayEndpoints[i++] = endpoint;
            return arrayEndpoints;
        }

        //
        // User optionally provides ShareName->VolumeName mapping to override volnames in config files. 
        // We believe ShareName->VolName mapping more than volnames in config files because Windows
        // sometimes changes the volumenames over wu/reboot where sharenames remain the same.
        //
        private Dictionary<string, string> InitDictShareNameToVolumeName(string configFilename)
        {
            Dictionary<string, string> dictShareNameToVolumeName = new Dictionary<string, string>();
            string inputRecord;
            string[] separators = new string[] { " ", "\t" };
            string aliasFileName = null;

            StreamReader configStreamIn = new StreamReader(configFilename);
            while ((inputRecord = configStreamIn.ReadLine()) != null)
            {
                string[] configTokens = inputRecord.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (configTokens.Length >= 2 && 
                    configTokens[0].ToLower().Equals(Parameters.VOLNAME_ALIAS_REC_HEADER))
                    aliasFileName = configTokens[1];
            }
            configStreamIn.Close();

            if (aliasFileName == null)
                return dictShareNameToVolumeName;
            if (!File.Exists(aliasFileName))
                throw new ApplicationException(string.Format("Invalid alias filename {0}", aliasFileName));

            StreamReader aliasStreamIn = new StreamReader(aliasFileName);
            while ((inputRecord = aliasStreamIn.ReadLine()) != null)
            {
                string[] inputTokens = inputRecord.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (inputTokens.Length == 0)
                    continue;
                else if (inputTokens[0].StartsWith(@"#") || inputTokens[0].StartsWith(@"//"))
                    continue;
                else if (inputTokens.Length < 2)
                    throw new ApplicationException(
                        string.Format("Invalid alias file rec < 2 fields {0} {1}", aliasFileName, inputRecord));
                else if (dictShareNameToVolumeName.ContainsKey(inputTokens[0].ToLower()))
                    throw new ApplicationException(
                        string.Format("Dup alias file key {0} {1}", aliasFileName, inputRecord));
                dictShareNameToVolumeName.Add(inputTokens[0].ToLower(), inputTokens[1]);
            }
            aliasStreamIn.Close();

            return dictShareNameToVolumeName;
        }

        //
        // Believe ShareName->VolName mapping more than volnames in config files.
        //
        private string GetVolumeNameFromShareName(string shareName, string volumeName)
        {
            if (DictShareNameToVolumeName.ContainsKey(shareName.ToLower()))
                return DictShareNameToVolumeName[shareName.ToLower()];
            return volumeName;
        }

        /// <summary>
        /// This method is called to initialize a rate controller from a
        /// given text input file, creating one Flow object for each input
        /// record that appears in th validTags argument. If the input file
        /// is valid this routine will proceed to connect and register with
        /// remote network agent processes, resolve VM names to Windows SIDs
        /// to wire up its internal data structures in preparation for
        /// future message handling.
        /// </summary>
        /// <param name="fileName">Name of input text config file.</param>
        /// <param name="validTags">Permitted input record types.</param>
        /// <returns>The list of Flow objects generated.</returns>
        public List<Flow> InitListFlows(string fileName, string[] validTags)
        {
            if (ListFlows != null)
                throw new ApplicationException("InitListFlows() can only be called once.");
            ListFlows = new List<Flow>();

            DictShareNameToVolumeName = InitDictShareNameToVolumeName(fileName);

            List<Endpoint> listEndpoints = ParseConfigFile(fileName,validTags);

            if (ListFlows.Count != 0)
            {
                InitListRap(listEndpoints);
                foreach (Flow flow in ListFlows)
                    AddTmesAndQueuesToFlow(flow);
            }
            DictEpKeyToRap = null; // Helper for AddTmesAndQueuesToFlow() : job done.

            //
            // Init our embedded Oktopus network rate controller.
            //
            SortedList<UInt64, EndpointDetails> netListEndpoints;
            netListEndpoints = netRateController.ParseConfigFile(fileName, validTags);
            if (netListEndpoints != null)
            {
                TrafficMatrix = netRateController.InitTrafficMatrix(netListEndpoints);

                //
                // Getting here means we have successfully registered with all network agents.
                // Find the set of matrix cells that represent VM-to-service edges, and those that
                // represent service-to-VM edges.
                //
                AllVMs = new List<TrafficMatrixEntry>();
                AllServices = new List<TrafficMatrixEntry>();
                for (int i = 0; i < netRateController.MatrixDimension; i++)
                    for (int j = 0; j < netRateController.MatrixDimension; j++)
                        if (TrafficMatrix[i, j].IsInterServer)
                            if (TrafficMatrix[i, j].LocEndpointDetails.Tag.Equals("vm") &&
                                TrafficMatrix[i, j].RemEndpointDetails.Tag.Equals("svc"))
                                AllVMs.Add(TrafficMatrix[i, j]);
                            else if (TrafficMatrix[i, j].LocEndpointDetails.Tag.Equals("svc") &&
                                     TrafficMatrix[i, j].RemEndpointDetails.Tag.Equals("vm"))
                                AllServices.Add(TrafficMatrix[i, j]);
                if (AllVMs.Count != AllServices.Count)
                    throw new ApplicationException(String.Format("AllVMs.Count({0}) != AllServices.Count({1})", AllVMs.Count, AllServices.Count));

                //
                // Now assign net flows to traffic matrix cells. This is a matter of policy decided here.
                // In this case one flow per matrix entry (no sharing of flows across cells or servers).
                // Note we have to tell Rate Controller on which server to install the flow.
                // Behind the scenes the Rate Controller is wiring up references from network 
                // connections structs to flows and matrix cells, and from flows to matrix cells.
                //
                ulong epsilon = 1514; // enough to get one packet through a flow.
                foreach (TrafficMatrixEntry tme in TrafficMatrix)
                {
                    // We do not control intra-VM traffic between VMs on the same server.
                    if (tme.IsInterServer)
                    {
                        tme.AssignFlow(netRateController.CreateFlow(tme.LocEndpointDetails.ServerName, epsilon));
                        string inputRec = tme.LocEndpointDetails.InputRecord;
                        string[] inputTokens = tme.LocEndpointDetails.InputTokens;
                        int lastHdrIdx = tme.LocEndpointDetails.InputTokensLastHeaderIndex;
                        ListFlows.Add(new Flow(tme, inputRec, inputTokens, lastHdrIdx));
                    }
                }
            }

            return ListFlows;
        }


        private string MakeVmKey(string serverName, string vmName)
        {
            return serverName + "." + vmName;
        }

        /// <summary>
        /// Called to initialize the list of RAPS.
        /// </summary>
        /// <param name="listEndpoints">List of (addr,NodeDetails) pairs typically ex ParseConfigFile().</param>
        /// <returns></returns>
        public List<RAP> InitListRap(List<Endpoint> listEndpoints)
        {
            ValidateState(RateControllerState.Init, "InitTrafficMatrix");
            
            if (listEndpoints.Count == 0)
                throw new ArgumentException("err: input cannot have zero length.");
            if (IsMatrixGenerated)
                throw new ApplicationException("err: InitListRap() can only be called once.");

            matrixDimension = listEndpoints.Count;

            //
            // Setup TCP connection to each network agent and then register with each network agent.
            // Each row in table represents a VM, so all entries in a row use the same network agent.
            //
            foreach (Endpoint ep in listEndpoints)
            {
                Connection conn;
                string serverName = ep.FilterServer;
                if (ep.IsOktofsH && !AgentNameToConn.TryGetValue(serverName, out conn))
                {
                    conn = new Connection(this, serverName, 0, AgentPort);
                    AgentNameToConn.Add(serverName, conn);
                    MessageRegister mRegister = new MessageRegister(++SeqNo, TenantId, AlertVec);
                    const MessageTypes replyType = MessageTypes.MessageTypeRegisterAck;
                    const int typeIndex = (int)replyType;
                    lock (LockPendingReplies[typeIndex])
                    {
                        // Registration messages actually sent sequentially, but using parallel paradigm.
                        // This because SendSequential will break if interleaved with alerts.
                        SendParallel(conn, mRegister.Serialize, replyType, mRegister.SeqNo);
                        conn.BeginReceive();
                        WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
                    }
                }
            }
            foreach (Endpoint ep in listEndpoints)
            {
                Connection conn;
                string hyperVName = ep.HyperVserver;
                //
                // Connections to remote OktofsAgent socket apps.
                //
                if ((ep.IsOktofsC || ep.IsOktofsH) && !AgentNameToConn.TryGetValue(hyperVName, out conn))
                {
                    conn = new Connection(this, hyperVName, 0, AgentPort);
                    AgentNameToConn.Add(hyperVName, conn);
                    MessageRegister mRegister = new MessageRegister(++SeqNo, TenantId, AlertVec);
                    const MessageTypes replyType = MessageTypes.MessageTypeRegisterAck;
                    const int typeIndex = (int)replyType;
                    lock (LockPendingReplies[typeIndex])
                    {
                        // Registration messages actually sent sequentially, but using parallel paradigm.
                        // This because SendSequential will break if interleaved with alerts.
                        SendParallel(conn, mRegister.Serialize, replyType, mRegister.SeqNo);
                        conn.BeginReceive();
                        WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
                    }
                }
                //
                // Connections to remote IoFlow socket apps.
                //
                if (ep.IsIoFlowD && !IoFlowNameToConn.TryGetValue(hyperVName, out conn))
                {
                    conn = new Connection(this, hyperVName, 0, Parameters.IOFLOWAGENT_TCP_PORT_NUMBER);
                    IoFlowNameToConn.Add(hyperVName, conn);
                    MessageRegister mRegister = new MessageRegister(++SeqNo, TenantId, AlertVec);
                    const MessageTypes replyType = MessageTypes.MessageTypeRegisterAck;
                    const int typeIndex = (int)replyType;
                    lock (LockPendingReplies[typeIndex])
                    {
                        // Registration messages actually sent sequentially, but using parallel paradigm.
                        // This because SendSequential will break if interleaved with alerts.
                        SendParallel(conn, mRegister.Serialize, replyType, mRegister.SeqNo);
                        conn.BeginReceive();
                        WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
                    }
                }
            }

            //
            // NT account names in the config file need to be translated into SIDs.
            // This because SIDS in config files are too unfriendly and error-prone.
            // Names and SIDS from an untrusted NT domain are legit, e.g. VMs in a workgroup.
            // Such names cannot be resolved locally, so ask the Hyper-V server to do it for us.
            // If the Hyper-V server is unknown, e.g. H-only config., config must use valid SIDS.
            //

            //
            // Ask Hyper-V servers to translate VM names to SIDS in case VM in untrusted NT domain.
            //
            Dictionary<string, Connection> DictIoFlowVmLookup = new Dictionary<string, Connection>(); // Any not resolved by Oktofs agent.
            foreach (Endpoint nDtls in listEndpoints)
            {
                // Nothing to do if config file claims to know the correct string SID.
                if (nDtls.SidOrAccount.ToUpper().StartsWith("S-1-5-"))
                    continue;
                // Only attempt SID translation at Hyper-V servers i.e. at point C in stack.
                if (!nDtls.Tag.ToUpper().Contains("-VM-SHARE-VOL") &&
                    !nDtls.Tag.ToUpper().Contains("-VM-FILE-VOL") )
                    continue;
                // Maybe we already queried this SID with the given Hyper-V server. 
                string VmKey = MakeVmKey(nDtls.HyperVserver, nDtls.SidOrAccount);
                if (DictVmNameToSid.ContainsKey(VmKey))
                {
                    nDtls.StringSid = DictVmNameToSid[VmKey];
                    continue;
                }
                // Ask remote Hyper-V server for the sid: our DC may not trust VMs NT domain (e.g. workgroup).
                Connection conn = null;
                if (AgentNameToConn.TryGetValue(nDtls.HyperVserver, out conn))
                {
                    MessageNameToStringSidQuery mQuerySid =
                        new MessageNameToStringSidQuery(++SeqNo, (uint)nDtls.SidOrAccount.Length, nDtls.SidOrAccount);
                    const MessageTypes replyType = MessageTypes.MessageTypeNameToStringSidReply;
                    const int typeIndex = (int)replyType;
                    lock (LockPendingReplies[typeIndex])
                    {
                        // For now (dbg) VmName to SID translation messages actually sent sequentially.
                        SendParallel(conn, mQuerySid.Serialize, replyType, mQuerySid.SeqNo);
                        WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
                    }
                    // Fatal error if SidQuery failed to update DictVmNameToSid.
                    if (!DictVmNameToSid.ContainsKey(VmKey))
                        throw new ApplicationException(String.Format("Panic: failed to get SID for {0}", VmKey));
                    continue;
                }
                // If we are using IoFlowAgent but not an OktoFsAgent try the IoFlowagent for the sid.
                if (IoFlowNameToConn.TryGetValue(nDtls.HyperVserver, out conn))
                {
                    if (!DictIoFlowVmLookup.ContainsKey(nDtls.SidOrAccount)){
                        DictIoFlowVmLookup.Add(nDtls.SidOrAccount, conn);
                    }
                }
            }

            // Any unresolved Vm-to-sid lookups are attempted via IoFlow network agent in batches in parallel.
            if (DictIoFlowVmLookup.Count != 0)
            {
                const MessageTypes replyType = MessageTypes.MessageTypeNameToStringSidBatchReply;
                const int typeIndex = (int)replyType;

                lock (LockPendingReplies[typeIndex])
                {
                    foreach (Connection conn in IoFlowNameToConn.Values)
                    {
                        List<string> listVmNames = new List<string>();
                        foreach (KeyValuePair<string, Connection> kvp in DictIoFlowVmLookup)
                            if (kvp.Value == conn)
                                listVmNames.Add(kvp.Key);
                        MessageNameToStringSidBatchQuery bq =
                            new MessageNameToStringSidBatchQuery(++SeqNo, listVmNames);
                        SendParallel(conn, bq.Serialize, replyType, bq.SeqNo);
                    }
                    WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
                } // lock
            }

            //
            // Set the SID for each RAP. Any trusted account names will be translated to SIDS at agent.
            //
            foreach (Endpoint nDtls in listEndpoints)
            {
                if (nDtls.SidOrAccount.ToUpper().StartsWith("S-1-5-"))
                    nDtls.StringSid = nDtls.SidOrAccount.ToUpper();
                else if (nDtls.Tag.ToUpper().Equals("H-HYPERV-VOL"))
                    nDtls.StringSid = nDtls.SidOrAccount.ToUpper();
                else
                    nDtls.StringSid = DictVmNameToSid[MakeVmKey(nDtls.HyperVserver, nDtls.SidOrAccount)];
            }

            //
            // Initialize RAPs and RAP lookup, and point Connections at each RAP.
            // More than one Connection may ref a RAP (e.g. Oktofs and IoFlow both).
            // Note RAPs not yet bound to flows.
            //
            ListRap = new List<RAP>();
            for (int i = 0; i < listEndpoints.Count; i++)
            {
                string locServername = listEndpoints[i].FilterServer;
                RAP rap = new RAP(listEndpoints[i], listEndpoints[i]);
                Connection conn = null;
                if (AgentNameToConn.TryGetValue(locServername, out conn))
                    conn.ListRap.Add(rap);
                if (IoFlowNameToConn.TryGetValue(locServername, out conn))
                    conn.ListRap.Add(rap);
                DictEpKeyToRap[listEndpoints[i].Key] = rap;
                ListRap.Add(rap);
            }

            Console.WriteLine("InitListRap made {0} RAPs", ListRap.Count);
            return ListRap;
        }

        /// <summary>
        /// Init TMEs and Queues within a Flow: 1 or 2 of each depends on whether Flow defines C and/or H.
        /// </summary>
        /// <param name="flow"></param>
        private void AddTmesAndQueuesToFlow(Flow flow)
        {
            ValidateState(RateControllerState.Init, "AddTmesAndQueuesToEdge");

            if (flow.EndpointC == null && flow.EndpointH == null)
                throw new ApplicationException("Flow cannot have both C and H are null.");

            // Find the RAP for each valid (non-null) Endpoint in the flow.
            // Wire the two RAP into the flow.
            if (flow.EndpointC != null && flow.EndpointC.IsOktofsC)
                flow.RapC = DictEpKeyToRap[flow.EndpointC.Key];
            if (flow.EndpointC != null && flow.EndpointC.IsIoFlowD)
                flow.RapD = DictEpKeyToRap[flow.EndpointC.Key];
            if (flow.EndpointH != null)
                flow.RapH = DictEpKeyToRap[flow.EndpointH.Key];

            Connection conn;
            uint flowId = NextFreeFlowId++;
            if (flow.EndpointC != null && 
                flow.EndpointC.IsOktofsC &&
                AgentNameToConn.TryGetValue(flow.EndpointC.FilterServer, out conn))
            {
                flow.RapC.AssignQueue(new OktoQueue(flowId, conn));     // so msg rx path can find dest flows.
                flow.RapC.Queue.SetFlagOn(OktoFlowFlags.FlowFlagIsAtC);
            }
            else if (flow.EndpointC != null && flow.EndpointC.IsOktofsC)
            {
                string msg = String.Format("AddQueuesToFlow({0}) unregistered agent C", flow.EndpointC.FilterServer);
                throw new ArgumentException(msg);
            }
            if (flow.EndpointH != null &&
                flow.EndpointH.IsOktofsH &&
                AgentNameToConn.TryGetValue(flow.EndpointH.FilterServer, out conn))
            {
                flow.RapH.AssignQueue(new OktoQueue(flowId, conn));     // so msg rx path can find dest flows.
                flow.RapH.Queue.SetFlagOn(OktoFlowFlags.FlowFlagIsAtH);
            }
            else if (flow.EndpointH != null && flow.EndpointH.IsOktofsH)
            {
                string msg = String.Format("AddQueuesToFlow({0}) unregistered agent H", flow.EndpointH.FilterServer);
                throw new ArgumentException(msg);
            }
            if (flow.EndpointC != null &&
                flow.EndpointC.IsIoFlowD &&
                IoFlowNameToConn.TryGetValue(flow.EndpointC.FilterServer, out conn))
            {
                flow.RapD.AssignQueue(new OktoQueue(flowId, conn)); // unused except by msg serializer.
                conn.DictIoFlows.Add(flow.FlowId, flow);            // so msg rx path can find dest flows.
            }
            else if (flow.EndpointC != null && flow.EndpointC.IsIoFlowD)
            {
                string msg = String.Format("AddQueuesToFlow({0}) unregistered IoFlow agent at HyperV ", flow.EndpointC.FilterServer);
                throw new ArgumentException(msg);
            }
            flow.SetDefaultFlags();
        }


        /// <summary>
        /// Called by client policy module to create a new queue.
        /// </summary>
        /// <param name="sourceAgentName">Name of network agent that is source for this queue,</param>
        /// <param name="bytesPerSecond">Initial rate limit to be associated with the queue.</param>
        /// <returns>Reference to new queue object if successful, otherwise returns null.</returns>
        public OktoQueue CreateQueue(string sourceAgentName)
        {
            OktoQueue queue = null;
            Connection conn;

            ValidateState(RateControllerState.Init, "CreateQueue");
            if (AgentNameToConn.TryGetValue(sourceAgentName, out conn))
            {
                queue = new OktoQueue(NextFreeFlowId++, conn);
            }
            else
            {
                string msg = String.Format("AddQueue({0},{1}) invalid args", sourceAgentName);
                throw new ArgumentException(msg);
            }
            return queue;
        }

        #endregion

        #region callbacks
        public void CallbackSettle()
        {
            throw new NotImplementedException("netRateController CallbackSettle should never get called.");
        }

        public void CallbackUpdate()
        {
            throw new NotImplementedException("netRateController CallbackUpdate should never get called.");
        }

        public void CallbackAlert(MessageNetAlert netAlert, NetRateControllerState state)
        {
            //
            // Turn the network alert into an oktofs alert and post it to work item queue for dispatch to policy module.
            //
            //throw new NotImplementedException("OktofsRateController CallbackAlert should never get called.");
            MessageAlert mAlert = MessageAlert.CreateFromNetAlert(netAlert);
            Log(String.Format("netAlert {0}", netAlert));
            RcWorkItem rcWorkItem = new RcWorkItem(RcWorkItemType.Alert, mAlert, 0, null);
            RcWorkQueue.Enqueue(rcWorkItem);
        }

        #endregion

        #region outbound message handlers
        private delegate void NetMessageSendProc();
        private class NetMessageBeginArgs
        {
            internal ManualResetEvent EventComplete;
            internal NetMessageSendProc SendProc;
            internal NetMessageBeginArgs(ManualResetEvent eventComplete,NetMessageSendProc sendProc)
            {
                EventComplete = eventComplete;
                SendProc = sendProc;
            }
        }
        private void NetMessagePairsProc(object context)
        {
            NetMessageBeginArgs args = (NetMessageBeginArgs)context;
            args.SendProc();
            args.EventComplete.Set();
        }
        private ManualResetEvent NetBeginMessagePairs(NetMessageSendProc messageSendProc)
        {
            ManualResetEvent EventMessagesComplete = new ManualResetEvent(false);
            NetMessageBeginArgs args = new NetMessageBeginArgs(EventMessagesComplete, messageSendProc);
            Thread NetMessagePairsThread = new Thread(NetMessagePairsProc);
            NetMessagePairsThread.Start(args);
            return EventMessagesComplete;
        }

        /// <summary>
        /// Sends messages to network agents instructing them to create flows. 
        /// The messages are sent synchronously in parallel and control is not returned to the
        /// caller until all network agents have replied.
        /// </summary>
        public void InstallFlows()
        {
            ValidateState(RateControllerState.Init, "InstallFlows");
            ManualResetEvent NetMessagesComplete = NetBeginMessagePairs(netRateController.InstallFlows);

            MessageTypes replyType = MessageTypes.MessageTypeFlowCreateAck;
            int typeIndex = (int)replyType;
            lock (LockPendingReplies[typeIndex])
            {
                foreach (Connection conn in AgentNameToConn.Values)
                {
                    if (conn.ListQueues.Count == 0)
                        continue;
                    OktoQueue[] arrayQosArg = new OktoQueue[conn.ListQueues.Count];
                    for (int i = 0; i < conn.ListQueues.Count; i++)
                        arrayQosArg[i] = conn.ListQueues[i];
                    Console.WriteLine("Installing {0} OktoFs flows on host {1}", arrayQosArg.Length, conn.HostName);
                    MessageFlowCreate mFlowCreate = new MessageFlowCreate(++SeqNo, arrayQosArg);
                    SendParallel(conn, mFlowCreate.Serialize, replyType, mFlowCreate.SeqNo);
                }
                WaitForParallelReplies(replyType, Parameters.FLOWC_MESSAGE_TIMEOUT_MS);
            } // lock

            replyType = MessageTypes.MessageTypeIoFlowCreateAck;
            typeIndex = (int)replyType;
            lock (LockPendingReplies[typeIndex])
            {
                foreach (Connection conn in IoFlowNameToConn.Values)
                {
                    if (conn.DictIoFlows.Count == 0)
                        continue;
                    Console.WriteLine("Installing {0} IoFlow flows on host {1}", conn.DictIoFlows.Count, conn.HostName);
                    List<Flow> ListIoFlows = conn.DictIoFlows.Values.ToList<Flow>();
                    MessageIoFlowCreate mIoFlowCreate = new MessageIoFlowCreate(++SeqNo, ListIoFlows);
                    SendParallel(conn, mIoFlowCreate.Serialize, replyType, mIoFlowCreate.SeqNo);
                }
                WaitForParallelReplies(replyType, Parameters.FLOWC_MESSAGE_TIMEOUT_MS);
            } // lock

            NetMessagesComplete.WaitOne();
            //
            // Optionally enable netRateController alert notification and/or RX stats recording.
            //
            UInt64 AlertMask = 0;
            //AlertMask |= (UInt64)OktoAlertType.AlertRxStats;
            //AlertMask |= (UInt64)OktoAlertType.AlertNoRap;     // Can be very noisy at agents.
            //AlertMask |= (UInt64)OktoAlertType.AlertNotActive;
            if (AlertMask != 0)
                netRateController.SetAlertVec(AlertMask);
        }


        /// <summary>
        /// Sends messages to network agents instructing them to create RAPs. 
        /// The messages are sent synchronously in parallel and control is not returned to the
        /// caller until all network agents have replied.
        /// </summary>
        public void InstallRaps()
        {
            ValidateState(RateControllerState.Init, "InstallRaps");

            ManualResetEvent NetMessagesComplete = NetBeginMessagePairs(netRateController.InstallRaps);

            const MessageTypes replyType = MessageTypes.MessageTypeRapFsCreateAck;
            const int typeIndex = (int)replyType;
            lock (LockPendingReplies[typeIndex])
            {
                foreach (Connection conn in AgentNameToConn.Values)
                {
                    List<RAP> listOktofsRap = new List<RAP>();

                    foreach (RAP rap in conn.ListRap)
                        if (rap.LocEndpointDetails.IsOktofsC || rap.LocEndpointDetails.IsOktofsH)
                            listOktofsRap.Add(rap);

                    if (listOktofsRap.Count > 0)
                    {
                        Console.WriteLine("InstallRaps {0} oktofs raps on host {1}", listOktofsRap.Count, conn.HostName);
                        RAP[] arrayRapFsArg = new RAP[listOktofsRap.Count];
                        for (int i = 0; i < listOktofsRap.Count; i++)
                            arrayRapFsArg[i] = conn.ListRap[i];
                        MessageRapFsCreate mRapFsCreate = new MessageRapFsCreate(++SeqNo, arrayRapFsArg);
                        SendParallel(conn, mRapFsCreate.Serialize, replyType, mRapFsCreate.SeqNo);
                    }
                }

                foreach (Connection conn in IoFlowNameToConn.Values)
                {
                    if (conn.DictIoFlows.Count == 0)
                        continue;
                    Console.WriteLine("InstallRaps {0} ioflow raps on host {1}", conn.DictIoFlows.Count, conn.HostName);
                    RAP[] arrayRapFsArg = new RAP[conn.DictIoFlows.Count];
                    int i = 0;
                    foreach (Flow flow in conn.DictIoFlows.Values)
                        arrayRapFsArg[i++] = flow.RapD;
                    MessageRapFsCreate mRapFsCreate = new MessageRapFsCreate(++SeqNo, arrayRapFsArg);
                    SendParallel(conn, mRapFsCreate.SerializeIoFlow, replyType, mRapFsCreate.SeqNo);
                }

                WaitForParallelReplies(replyType, Parameters.RAPC_MESSAGE_TIMEOUT_MS);
            } // lock

            NetMessagesComplete.WaitOne();
        }

        /// <summary>
        /// Sends messages to network agents instructing them to update queues with 
        /// the values presently stored in the Queue objects. Update messages for
        /// queues whose rate limit has not changed since the last time this routine
        /// was called are suppressed. The messages are sent synchronously in parallel
        /// and control is not returned to the caller until all network agents have replied.
        /// </summary>
        public void UpdateRateLimits()
        {
            ValidateState(RateControllerState.UpdateCallback, "UpdateRateLimits");
            ManualResetEvent NetMessagesComplete = NetBeginMessagePairs(netRateController.UpdateRateLimits);

            MessageTypes replyType = MessageTypes.MessageTypeFlowUpdateAck;
            int typeIndex = (int)replyType;
            lock (LockPendingReplies[typeIndex])
            {
                //
                // Send update messages only wrt queues whose rate limits have been changed.
                //
                foreach (Connection conn in AgentNameToConn.Values)
                {
                    int countQueues = 0;
                    foreach (OktoQueue queue in conn.ListQueues)
                        if (queue.IsChanged)
                            countQueues++;
                    if (countQueues == 0)
                        continue;
                    OktoQueue[] arrayQosArg = new OktoQueue[countQueues];
                    int i = 0;
                    foreach (OktoQueue queue in conn.ListQueues)
                    {
                        if (queue.IsChanged)
                        {
                            queue.Update();
                            arrayQosArg[i++] = queue;
                        }
                    }
                    MessageFlowUpdate mFlowUpdate = new MessageFlowUpdate(++SeqNo, arrayQosArg);
                    SendParallel(conn, mFlowUpdate.Serialize, replyType, mFlowUpdate.SeqNo);
                }
                WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
            } // lock

            replyType = MessageTypes.MessageTypeIoFlowUpdateAck;
            typeIndex = (int)replyType;
            lock (LockPendingReplies[typeIndex])
            {
                //
                // Send update messages for IoFlow queues.
                // Cannot filter unchanged as we don't know how to interpret free-format strings.
                //
                foreach (Connection conn in IoFlowNameToConn.Values)
                {
                    if (conn.DictIoFlows.Count == 0)
                        continue;

                    List<Flow> listFlow = conn.DictIoFlows.Values.ToList<Flow>();
                    MessageIoFlowUpdate mFlowUpdate = new MessageIoFlowUpdate(++SeqNo, listFlow);
                    SendParallel(conn, mFlowUpdate.Serialize, replyType, mFlowUpdate.SeqNo);
                }
                WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
            } // lock

            NetMessagesComplete.WaitOne();
        }

        /// <summary>
        /// Sends messages to network agents instructing them to set traffic stats
        /// to zero. The messages are sent synchronously in parallel and control
        /// is not returned to the caller until all network agents have replied.
        /// </summary>
        public void ResetStats()
        {
            const MessageTypes replyType = MessageTypes.MessageTypeStatsZeroAck;
            const int typeIndex = (int)replyType;

            ValidateState(RateControllerState.SettleCallback, "ResetStats");
            ManualResetEvent NetMessagesComplete = NetBeginMessagePairs(netRateController.ResetStats);
            lock (LockPendingReplies[typeIndex])
            {
                foreach (Connection conn in AgentNameToConn.Values)
                {
                    if (conn.ListQueues.Count == 0)
                        continue;
                    MessageStatsZero mStatsZero = new MessageStatsZero(++SeqNo);
                    SendParallel(conn, mStatsZero.Serialize, replyType, mStatsZero.SeqNo);
                }
                foreach (Connection conn in IoFlowNameToConn.Values)
                {
                    if (conn.DictIoFlows.Count == 0)
                        continue;
                    MessageStatsZero mStatsZero = new MessageStatsZero(++SeqNo);
                    SendParallel(conn, mStatsZero.Serialize, replyType, mStatsZero.SeqNo);
                }
                WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
            } // lock
            NetMessagesComplete.WaitOne();
        }


        private delegate void NetMessageStatsProc(double deltaThreshold, bool txStats, bool rxStats, bool reset, bool queueLength);
        private class NetMessageStatsArgs
        {
            internal ManualResetEvent EventComplete;
            internal NetMessageStatsProc SendProc;
            double DeltaThreshold;
            bool TxStats , RxStats, Reset, QueueLength;

            internal NetMessageStatsArgs( ManualResetEvent eventComplete, NetMessageStatsProc sendProc,
                double deltaThreshold, bool txStats, bool rxStats, bool reset, bool queueLength )
            {
                EventComplete = eventComplete;
                SendProc = sendProc;
                DeltaThreshold = deltaThreshold;
                TxStats = txStats;
                RxStats = rxStats;
                Reset = reset;
                QueueLength = queueLength;
            }
            internal void SendMessages()
            {
                SendProc(DeltaThreshold, TxStats, RxStats, Reset, QueueLength);
            }
        }
        private void NetMessageStatsThreadProc(object context)
        {
            NetMessageStatsArgs args = (NetMessageStatsArgs)context;
            args.SendMessages();
            args.EventComplete.Set();
        }
        private ManualResetEvent NetBeginStatsMessages(
            NetMessageStatsProc messageStatsProc,
            double deltaThreshold, bool txStats, bool rxStats, bool reset, bool queueLength)
        {
            ManualResetEvent EventMessagesComplete = new ManualResetEvent(false);
            NetMessageStatsArgs args = 
                new NetMessageStatsArgs(EventMessagesComplete, messageStatsProc,
                                        deltaThreshold, txStats, rxStats, reset, queueLength);
            Thread NetMessageStatsThread = new Thread(NetMessageStatsThreadProc);
            NetMessageStatsThread.Start(args);
            return EventMessagesComplete;
        }

        /// <summary>
        /// Sends messages to network agents instructing them to send back traffic
        /// stats. The messages are sent synchronously in parallel and control
        /// is not returned to the caller until all network agents have replied. 
        /// The request can include a delta threshold expressed as a percentage
        /// in which case agents will filter out state for RAPs whose current 
        /// bandwidth falls within the last reported bandwidth plus or minus delta.
        /// </summary>
        /// <param name="deltaThreshold">Stats suppression threshold.</param>
        /// <param name="reset">True iff stats to be set to zero once read.</param>
        public void GetStatsFromServers(double deltaThreshold, bool reset)
        {
            MessageTypes replyType = MessageTypes.MessageTypeStatsReplyDelta;
            int typeIndex = (int)replyType;

            ManualResetEvent NetMessagesComplete =
                NetBeginStatsMessages(netRateController.UpdateTrafficStatsDelta,
                                      deltaThreshold, true, false, reset, true);
            lock (LockPendingReplies[typeIndex])
            {
                foreach (Connection conn in AgentNameToConn.Values)
                {
                    if (conn.ListQueues.Count == 0)
                        continue;
                    uint countRaps = (uint)conn.ListRap.Count;
                    MessageStatsQueryDelta mStatsQuery =
                        new MessageStatsQueryDelta(++SeqNo, countRaps, deltaThreshold, reset);
                    SendParallel(conn, mStatsQuery.Serialize, replyType, mStatsQuery.SeqNo);
                }
                WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
            } // lock

            replyType = MessageTypes.MessageTypeIoFlowStatsReply;
            typeIndex = (int)replyType;
            lock (LockPendingReplies[typeIndex])
            {
                foreach (Connection conn in IoFlowNameToConn.Values)
                {
                    if (conn.DictIoFlows.Count == 0)
                        continue;
                    MessageIoFlowStatsQuery mIoFlowStatsQuery = new MessageIoFlowStatsQuery(++SeqNo);
                    SendParallel(conn, mIoFlowStatsQuery.Serialize, replyType, mIoFlowStatsQuery.SeqNo);
                }
                WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
            } // lock

            NetMessagesComplete.WaitOne();
        }


        /// <summary>
        /// Sends messages to network agents instructing them to delete all queues 
        /// and their associated RAPs for the specified TenantId. The messages are
        /// sent synchronously in parallel and control is not returned to the
        /// caller until all network agents have replied.
        /// </summary>
        private void DeleteTenant()
        {
            const MessageTypes replyType = MessageTypes.MessageTypeTenantDeleteAck;
            const int typeIndex = (int)replyType;

            ValidateState(RateControllerState.Fin, "DeleteTenant");
            ManualResetEvent NetMessagesComplete = NetBeginMessagePairs(netRateController.DeleteTenant);
            lock (LockPendingReplies[typeIndex])
            {
                foreach (Connection conn in AgentNameToConn.Values)
                {
                    if (conn.ListQueues.Count > 0)
                    {
                        MessageTenantDelete mTenantDelete = new MessageTenantDelete(++SeqNo);
                        SendParallel(conn, mTenantDelete.Serialize, replyType, mTenantDelete.SeqNo);
                    }
                }
                WaitForParallelReplies(replyType, Parameters.DEFAULT_MESSAGE_TIMEOUT_MS);
            } // lock
            NetMessagesComplete.WaitOne();
        }
        #endregion

        #region control state (old loop and new machine)
        /// <summary>
        /// Transitions state machine. Called from sequential work queue in response to timeouts and alerts. 
        /// </summary>
        /// <param name="nextState"></param>
        public void StateTransition(RateControllerState nextState)
        {
            RcWorkItem context;
            lock (LockState)
            {
                Debug.WriteLine("StateTransition {0}=>{1}", state, nextState);
                if (state.Equals(RateControllerState.Fin) ||
                    nextState.Equals(RateControllerState.Fin) ||
                    Shutdown)
                {
                    state = RateControllerState.Fin;
                    return;
                }

                //
                // Transition to new state and take the actions associated with that state. 
                //
                switch (state)
                {
                    case RateControllerState.Init:
                        Debug.Assert(nextState == RateControllerState.SettleTimeout);
                        state = nextState;
                        if (netRateController != null)
                            netRateController.StateTransitionNoOp(NetRateControllerState.SettleTimeout);
                        context = new RcWorkItem(RcWorkItemType.SettleTimeout, RateControllerState.SettleTimeout, settleMillisecs, null);
                        Console.WriteLine("RateController start init timer {0} millisecs", RcWorkItemType.SettleTimeout);
                        StartTimer(settleMillisecs, context);
                        break;
                    case RateControllerState.SettleTimeout:
                        Debug.Assert(nextState == RateControllerState.SettleCallback);
                        state = nextState;
                        if (netRateController != null)
                            netRateController.StateTransitionNoOp(NetRateControllerState.SettleCallback);
                        iPolicyModule.CallbackSettleTimeout();
                        break;
                    case RateControllerState.SettleCallback:
                        Debug.Assert(nextState == RateControllerState.SampleTimeout);
                        state = nextState;
                        if (netRateController != null)
                            netRateController.StateTransitionNoOp(NetRateControllerState.SampleTimeout);
                        context = new RcWorkItem(RcWorkItemType.SampleTimeout, RateControllerState.SampleTimeout, sampleMillisecs, null);
                        StartTimer(sampleMillisecs, context);
                        break;
                    case RateControllerState.SampleTimeout:
                        Debug.Assert(nextState == RateControllerState.UpdateCallback);
                        state = nextState;
                        if (netRateController != null)
                            netRateController.StateTransitionNoOp(NetRateControllerState.UpdateCallback);
                        iPolicyModule.CallbackControlInterval();
                        break;
                    case RateControllerState.UpdateCallback:
                        Debug.Assert(nextState == RateControllerState.SettleTimeout);
                        state = nextState;
                        if (netRateController != null)
                            netRateController.StateTransitionNoOp(NetRateControllerState.SettleTimeout);
                        context = new RcWorkItem(RcWorkItemType.SettleTimeout, RateControllerState.SettleTimeout, settleMillisecs, null);
                        StartTimer(settleMillisecs, context);
                        break;
                }
            }
        }

        private void ValidateState(RateControllerState expectState, string procedure)
        {
            lock (LockState)
            {
                if (state != expectState)
                {
                    string message = "Rate Controller in invalid state for call to ";
                    message = String.Format("{0} {1} : current {2} expected {3}", message, procedure, state, expectState);
                    throw new ApplicationException(message);
                }
            }
        }

        /// <summary>
        /// Called by Policy Module to register callbacks and pass control to the
        /// rate controller's state machine, which operates on a single thread.
        /// The Policy Module's main thread will block in this routine until the
        /// state machine enters the shutdown state.
        /// </summary>
        public void Start()
        {
            //
            // Start run.
            //
            lock (LockState)
            {
                Console.WriteLine("gregos rate controller start() state = {0}", state);
                // Alert arrivals may have already advanced the state.
                // Fine if so, otherwise transition now.
                if (state == RateControllerState.Init)
                {
                    Console.WriteLine("gregos rate controller start() transition to SettleTimeout");
                    StateTransition(RateControllerState.SettleTimeout);
                }
                else
                {
                    Console.WriteLine("gregos rate controller start() transition is no-op");
                }
            }

            netRateController.Start(true);

            //
            // Pause the Policy Module main thread until we enter shutdown state.
            //
            Console.WriteLine("Blocked main thread (good).");
            EventPolicyModuleThreadBlocked.WaitOne();
        }

        /// <summary>
        /// Starts the Timeout timer.
        /// </summary>
        /// <param name="dueTime"></param>
        private void StartTimer(long millisecsFromNow, RcWorkItem context)
        {
            lock (LockTimer)
            {
                CurrentTimeoutUSecs = softTimer.TimeNowUSecs() + (millisecsFromNow * 1000);
                context.TimerUSecs = CurrentTimeoutUSecs;
                softTimer.RegisterTimer(CurrentTimeoutUSecs, context);
            }
        }

        /// <summary>
        /// We need to ignore timeouts that have been overtaken by alert provessing.
        /// </summary>
        /// <param name="timerUSecs"></param>
        /// <returns></returns>
        private bool IsCurrentTimeout(long timerUSecs)
        {
            lock (LockTimer)
            {
                return (timerUSecs == CurrentTimeoutUSecs);
            }
        }

        /// <summary>
        /// Single thread running work queue service routine ensures sequential callbacks into Policy Module.
        /// </summary>
        /// <param name="workItem"></param>
        private void RunRcWorkQueueItem(RcWorkItem workItem)
        {
            switch (workItem.Type)
            {
                case RcWorkItemType.SettleTimeout:
                    if (IsCurrentTimeout(workItem.TimerUSecs))
                    {
                        StateTransition(RateControllerState.SettleCallback);
                        StateTransition(RateControllerState.SampleTimeout);
                    }
                    else
                    {
                        smsg("ignore stale TO IntervalSettleExpired");
                    }
                    break;
                case RcWorkItemType.SampleTimeout:
                    if (IsCurrentTimeout(workItem.TimerUSecs))
                    {
                        StateTransition(RateControllerState.UpdateCallback);
                        StateTransition(RateControllerState.SettleTimeout);
                    }
                    else
                    {
                        smsg("ignore stale TO IntervalSampleExpired");
                    }
                    break;
                case RcWorkItemType.Alert:
                    // Note: the alert callback may (have to) adjust current RC state.
                    iPolicyModule.CallbackAlert((MessageAlert)workItem.Args, State);
                    break;
            }
        }
        #endregion

        #region message-pair support

        /// <summary>
        /// Send given message on given connection then block until a (n)ack is received.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message is to be sent.</param>
        /// <param name="funcPtrSerialize"></param>
        /// <returns></returns>
        private bool SendSequential(Connection conn, FuncPtrSerialize funcPtrSerialize, uint seqNo)
        {
            int countBytesToSend = funcPtrSerialize(conn.sendBuffer, 0);
            int countBytesSent = conn.Send(conn.sendBuffer, 0, countBytesToSend);
            if (countBytesSent != countBytesToSend)
            {
                Console.WriteLine("SendSynchronous Err: attempt {0} sent {1}.", countBytesToSend, countBytesSent);
                return false;
            }
            int countRecv = conn.Recv(conn.receiveBuffer, 0, (int)MessageAck.TOTAL_MESSAGE_SIZE);
            if (countRecv != MessageAck.TOTAL_MESSAGE_SIZE)
            {
                Console.WriteLine("SendSynchronous Err: attempt {0} recv {1}.", MessageAck.TOTAL_MESSAGE_SIZE, countRecv);
                return false;
            }
            conn.MessageAck.InitFromNetBytes(conn.receiveBuffer, 0);
            Console.WriteLine("SendSynchronous rcv MessageAck({0},{1})", conn.MessageAck.SeqNo, conn.MessageAck.Result);
            if (conn.MessageAck.SeqNo != seqNo)
            {
                Console.WriteLine("SendSynchronous Err: SN {0} != expected {1}.", conn.MessageAck.SeqNo, seqNo);
                return false;
            }
            return true;
        }


        /// <summary>
        /// Send given message on given connection, start async receive and immediately return to caller.
        /// Caller must hold LockPendingReplies for the reply message type.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message is to be sent.</param>
        /// <param name="funcPtrSerialize">Serilaize() method of the message we want to send.</param>
        /// <returns>True on success, false on error.</returns>
        private bool SendParallel(Connection conn, FuncPtrSerialize funcPtrSerialize, MessageTypes replyType, uint seqNo)
        {
            DictPendingReplies.Add(seqNo, replyType);
            Interlocked.Increment(ref CountPendingReplies[(int)replyType]);
            int countBytesToSend = funcPtrSerialize(conn.sendBuffer, 0);
            int countBytesSent = conn.Send(conn.sendBuffer, 0, countBytesToSend);
            if (countBytesSent != countBytesToSend)
            {
                Console.WriteLine("SendParallel Err: attempt {0} sent {1}.", countBytesToSend, countBytesSent);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Waits until expected number of messages of given type have been received.
        /// Throws if messages not received withing given timeout: this to prevent
        /// infinite wait if remote network agent app is wedged with its TCP conn alive.
        /// </summary>
        /// <param name="replyMessageType"></param>
        private void WaitForParallelReplies(MessageTypes replyMessageType, int timeout)
        {
            int typeIndex = (int)replyMessageType;
            lock (LockPendingReplies[typeIndex])
            {
                while (CountPendingReplies[typeIndex] != 0)
                    if (Monitor.Wait(LockPendingReplies[typeIndex], timeout) == false)
                        throw new ApplicationException(String.Format("Timeout reply message type {0}", typeIndex));
            }
        }

        /// <summary>
        /// Called on the message receive path to maintain the list of message pairs on which a
        /// thread may have blocked itself by calling the WaitForPendingReplies() method.
        /// </summary>
        /// <param name="msgType">Message type of the inbound message.</param>
        /// <param name="seqNo">Sequence number of the inbound message is same as the
        /// sequence number of the outbound message to which it is replying.</param>
        /// <returns></returns>
        private bool DowndatePendingReplies(MessageTypes msgType, uint seqNo)
        {
            bool result = false;

            lock (LockPendingReplies[(int)msgType])
            {
                MessageTypes expectedType = MessageTypes.MessageTypeIllegal;
                if (DictPendingReplies.TryGetValue(seqNo, out expectedType))
                {
                    if (msgType != expectedType)
                    {
                        string msg = String.Format("Err: reply message type {0} (expected{1})", msgType, expectedType);
                        throw new ApplicationException(msg);
                    }
                    DictPendingReplies.Remove(seqNo);
                    int countPending = Interlocked.Decrement(ref CountPendingReplies[(int)msgType]);
                    if (countPending == 0)
                    {
                        Monitor.Pulse(LockPendingReplies[(int)msgType]);
                        result = true;
                    }
                    else if (countPending < 0)
                    {
                        string msg = String.Format("Err: count pending message type {0} gone negative.", msgType);
                        throw new ApplicationException(msg);
                    }
                }
                else
                {
                    string msg = String.Format("Err: received unexpected SeqNo {0} msgType {1}.", seqNo, msgType);
                    throw new ApplicationException(msg);
                }
            }

            return result;
        }

        #endregion

        #region inbound message handlers

        /// <summary>
        /// Called by a connection when it has received an intact and complete message in wire-format.
        /// Parses the supplied byte-array to generate a typed message for processing.
        /// On return from this routine the connection is free to overwrite the buffer contents.
        /// /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message arrived.</param>
        /// <param name="buff">Buffer encoding the message.</param>
        /// <param name="offset">Offset to start of message in the supplied buffer.</param>
        /// <param name="length">Length of message encoding in supplied buffer</param>
        public void ReceiveMessage(Connection conn, MessageTypes messageType, byte[] buff, int offset, int length)
        {
            bool unblock = false;
            switch (messageType)
            {
                case MessageTypes.MessageTypeAck:
                    {
                        conn.MessageAck.InitFromNetBytes(buff, 0);
                        //Console.WriteLine("SendParallel rcv MessageAck({0},{1})", conn.MessageAck.SeqNo, conn.MessageAck.Result);
                        if (conn.MessageAck.Result != (uint)OktoResultCodes.OKTO_RESULT_SUCCESS)
                        {
                            string msg = String.Format("ACK err: conn {0} ResultCode {1} subytpe {2}",
                                conn.HostName, conn.MessageAck.Result, (MessageTypes)conn.MessageAck.SubType);
                            Console.WriteLine(msg);
                        }
                        MessageTypes AckSubType = (MessageTypes)conn.MessageAck.SubType;
                        unblock = DowndatePendingReplies(AckSubType, conn.MessageAck.SeqNo);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeStatsReplyDelta:
                    {
                        MessageStatsReplyDelta mStatsDelta = MessageStatsReplyDelta.CreateFromNetBytes(conn.receiveBuffer, 0, conn, this);
                        //Console.WriteLine("ReceiveRaw MessageStatsReplyDelta({0},{1})", mStatsDelta.SeqNo, mStatsDelta.Result);
                        ulong intervalUSecs = mStatsDelta.IntervalUSecs;

                        foreach (KeyValuePair<ushort, FlowStats> kvp in mStatsDelta.dictFlowStats)
                        {
                            RAP rap = conn.ListRap[kvp.Key];
                            FlowStats flowStats = kvp.Value;
                            rap.SetFlowStats(flowStats);
                        }
                        unblock = DowndatePendingReplies(messageType, mStatsDelta.SeqNo);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeNameToStringSidReply:
                    {
                        MessageVmNameToStringSidReply mNameToSidReply 
                            = MessageVmNameToStringSidReply.CreateFromNetBytes(conn.receiveBuffer, 0);
                        string vmKey = conn.HostName + "." + mNameToSidReply.VmName;
                        lock (DictVmNameToSid)
                        {
                            if (!DictVmNameToSid.ContainsKey(vmKey))
                                DictVmNameToSid.Add(vmKey, mNameToSidReply.SidString);
                        }
                        unblock = DowndatePendingReplies(messageType, mNameToSidReply.SeqNo);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeAlert:
                    {
                        MessageAlert mAlert = MessageAlert.CreateFromNetBytes(conn.receiveBuffer, 0);
                        //Console.WriteLine("ReceiveMessage rx alert {0}", mAlert);
                        // Push message to the sequential work queue for upcall into Policy Module.
                        RcWorkItem rcWorkItem = new RcWorkItem(RcWorkItemType.Alert, mAlert, 0, conn);
                        RcWorkQueue.Enqueue(rcWorkItem);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeIoFlowStatsReply:
                    {
                        MessageIoFlowStatsReply mIoFlowStats =
                            MessageIoFlowStatsReply.CreateFromNetBytes(conn.receiveBuffer, 0);
                        foreach (IoFlowMessageParams stats in mIoFlowStats.ListParams)
                            conn.DictIoFlows[stats.FlowId].RapD.SetIoFlowStats(stats.ParameterString);
                        unblock = DowndatePendingReplies(messageType, mIoFlowStats.SeqNo);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeNameToStringSidBatchReply:
                    {
                        MessageVmNameToStringSidBatchReply mNameToSidReply
                            = MessageVmNameToStringSidBatchReply.CreateFromNetBytes(conn.receiveBuffer, 0);
                        lock (DictVmNameToSid)
                        {
                            foreach (KeyValuePair<string, string> kvp in mNameToSidReply.DictVmNameToSid)
                            {
                                string vmKey = MakeVmKey(conn.hostName, kvp.Key);
                                if (!DictVmNameToSid.ContainsKey(vmKey))
                                {
                                    Console.WriteLine("sidBatchReply adding ({0},{1}", vmKey, kvp.Value);
                                    DictVmNameToSid.Add(vmKey, kvp.Value);
                                }
                            }
                        }
                        unblock = DowndatePendingReplies(messageType, mNameToSidReply.SeqNo);
                        conn.BeginReceive();
                        break;
                    }

                default:
                    {
                        string msg = string.Format("SendParallel: unexpected message type {0}", messageType);
                        throw new ApplicationException(msg);
                    }
                //break;
            }
        }

        /// <summary>
        /// Called by a Connection (TCP to a specific network agent) when it encounters a TCP receive error.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message arrived.</param>
        public void ReceiveError(Connection conn, int errNo)
        {
            string msg = String.Format("OktofsRateController comms error: conn = {0}, err = {1}", conn.HostName, errNo);
            throw new ApplicationException(msg);
        }

        /// <summary>
        /// Callback invoked by a connection catches a socket exception.
        /// </summary>
        /// <param name="conn">Connection that caught the socket exception.</param>
        /// <param name="sockEx">The socket exception.</param>
        public void CatchSocketException(Connection conn, SocketException sockEx)
        {
            string msg = String.Format("OktofsRateController.CatchSocketException({0},{1})", conn.HostName, sockEx.Message);
            throw sockEx;
        }


        /// <summary>
        /// Called by a Connection (TCP to a specific network agent) when TCP has closed the connection.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message arrived.</param>
        public void ReceiveClose(Connection conn)
        {
            Console.WriteLine("RM shutdown: received close (zero bytes) from conn {0}", conn.HostName);
            StateTransition(RateControllerState.Fin);
#if gregos // tidy RC shutdown exceptions
            //DeleteTenant();
            foreach (Connection connection in AgentNameToConn.Values)
                if (connection != conn)
                    conn.Close();
            AgentNameToConn = null;
#endif
            EventPolicyModuleThreadBlocked.Set();
        }

        #endregion

        #region utils

        public static uint BytesPerTtoBytesPerSec(uint bpt, uint Tms)
        {
            double dBps = bpt;
            return (uint)(dBps * (1000.0 / (double)Tms));
        }

        public static uint BytesPerSecToBytesPerT(uint bps, uint Tms)
        {
            double dBpt = bps;
            return (uint)(dBpt * ((double)Tms / 1000.0));
        }

        public long TimeNowUSecs()
        {
            return softTimer.TimeNowUSecs();
        }

        public void smsg(string msg)
        {
            Console.WriteLine("{0}: {1}", TimeNowUSecs(), msg);
        }

        public Device FindOrCreateDevice(string hostName, string deviceName)
        {
            Device device;
            string fullName = @"\\" + hostName.ToLower() + deviceName.ToLower();
            lock (DictDevice)
            {
                if (DictDevice.ContainsKey(fullName))
                {
                    device = DictDevice[fullName];
                }
                else
                {
                    device = new Device(hostName, deviceName);
                    DictDevice.Add(fullName, device);
                }
            }
            return device;
        }

        /// <summary>
        /// Opens a new log file as the destination for future calls to the Log() method.
        /// If no file name is supplied then a new file will be opened in the \tmp\oktofslogs
        /// folder with the name RateController_YYYYMMDD_MMSS.log 
        /// </summary>
        /// <param name="logFileName">Name of log file.</param>
        /// <returns>Name of the log file that was opened.</returns>
        public string NewLogFile(string logFileName)
        {
            if (logStream != null)
            {
                logStream.Flush();
                logStream.Close();
            }
            if (string.IsNullOrEmpty(logFileName) || string.IsNullOrWhiteSpace(logFileName))
            {
                if (Directory.Exists(Parameters.TMP_DIR) == false)
                    Directory.CreateDirectory(Parameters.TMP_DIR);
                if (Directory.Exists(Parameters.LOG_DIR) == false)
                    Directory.CreateDirectory(Parameters.LOG_DIR);
                string date = DateTime.Now.ToShortDateString();
                string time = DateTime.Now.ToLongTimeString();
                //string FileTimeString = date.Substring(6, 4) + date.Substring(3, 2) +
                //                        date.Substring(0, 2) + "_" + time.Replace(":", "");
                string FileTimeString = date.Replace("/","_") + time.Replace(":", "");
                logFileName = Parameters.LOG_DIR + @"\RateController_" + FileTimeString + @".log";
            }

            logStream = new StreamWriter(logFileName);
            return logFileName;
        }

        /// <summary>
        /// Writes the supplied string as a new line in the current log file.
        /// If no log file has been explicitly opened then an implicit call 
        /// to NewLogFile() is made to open a log file with the default name. 
        /// </summary>
        /// <param name="s">Text record to be written to log file.</param>
        public void Log(string s)
        {
            // guard against multiple threads from concurrent user threads.
            lock (LockLog)
            {
                if (logStream == null)
                    NewLogFile(null);
                Debug.Assert(logStream != null);
                logStream.WriteLine(s);
                Console.WriteLine(s);
                logStream.Flush();
            }
        }

        public void PostAlert(OktoAlertType alertType, ulong[] args)
        {
            // Push message to the sequential work queue for upcall into Policy Module.
            MessageAlert mAlert = new MessageAlert(alertType,args);
            RcWorkItem rcWorkItem = new RcWorkItem(RcWorkItemType.Alert, mAlert, 0, null);
            RcWorkQueue.Enqueue(rcWorkItem);
        }

        public void PostAlertMessage(byte[] buffer, int offset)
        {
            // Parse and push message to the sequential work queue for upcall into Policy Module.
            MessageAlert mAlert = MessageAlert.CreateFromNetBytes(buffer,offset);
            RcWorkItem rcWorkItem = new RcWorkItem(RcWorkItemType.Alert, mAlert, 0, null);
            RcWorkQueue.Enqueue(rcWorkItem);
        }

        public long TimeNowUSecsQpc()
        {
            return qpc.TimeNowUSecs();
        }


        #endregion

        #region IDisposable

        //
        // CodeAnalysis requires we implement IDisposable due to use of ManualResetEvent
        //
        bool disposed = false;

        public void Close()
        {
            if (logStream != null)
            {
                logStream.Flush();
                logStream.Close();
            }
            Shutdown = true;
            Dispose();
        }

        /// <summary>
        /// Ref documentation for IDisposable interface.
        /// Do not make this method virtual.
        /// A derived class should not be able to override this method.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SupressFinalize to
            // take this object off the finalization queue
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        // Dispose(bool disposing) executes in two distinct scenarios.
        // If disposing equals true, the method has been called directly
        // or indirectly by a user's code. Managed and unmanaged resources
        // can be disposed.
        // If disposing equals false, the method has been called by the
        // runtime from inside the finalizer and you should not reference
        // other objects. Only unmanaged resources can be disposed.
        void Dispose(bool calledByUserCode)
        {
            // Check to see if Dispose has already been called.
            if (!this.disposed)
            {
                if (calledByUserCode)
                {
                    if (netRateController != null)
                        netRateController.Close();

                    //
                    // Dispose managed resources here.
                    //
                    foreach (Connection conn in AgentNameToConn.Values)
                        conn.Close();
                    AgentNameToConn.Clear();
                }

                // 
                // Dispose unmanaged resources here.
                //

            }
            disposed = true;
        }

        // Use C# destructor syntax for finalization code.
        // This destructor will run only if the Dispose method 
        // does not get called.
        ~OktofsRateController()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        #endregion
    }
}
