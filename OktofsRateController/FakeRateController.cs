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

namespace RateControllerNamespace
{
    /// <summary>
    /// This module and the classes in it are just a fake of the Oktopus "RateController" class.
    /// It exists only so we can build and release OktofsRateController without its embedded RateController.
    /// This is because we haven't used the network rate control for a while and don't want to test it now.
    /// Also we don't want to figure out how to make setup.exe deal with the oktopus.sys NDIS LWF either.
    /// To get back the network rate control first exclude this class from the OktofsRateController project.
    /// Then add the real RateController class into the sln as an existing project and add refs to it.
    /// </summary>
    public class RateController
    {
        public int MatrixDimension = 0;
        public RateController(INetPolicyModule client, uint tenantId, int agentPort) { }
        public void Start(bool calledFromOktofs) { }
        public void InstallFlows() { }
        public void InstallRaps() { }
        public void UpdateRateLimits() { }
        public void ResetStats() { }
        public void UpdateTrafficStatsDelta(
            double deltaThreshold, bool txStats, bool rxStats, bool reset, bool queueLength) { }
        public void DeleteTenant() { }
        public void StateTransitionNoOp(NetRateControllerState nextState) { }
        public void SetAlertVec(UInt64 alertVec) { }
        public SortedList<UInt64, EndpointDetails> ParseConfigFile(string fileName, string[] validTags)
        {
            return null;
        }
        public RateControllerFlow CreateFlow(string sourceAgentName, ulong bytesPerSecond) {  return null; }
        public TrafficMatrixEntry[,] InitTrafficMatrix(SortedList<UInt64, EndpointDetails> listEndpoints)
        {
            return null;
        }
        public void Close() { }
    }

    public class EndpointDetails
    {
        public string Tag = "";
        public string ServerName = "";
        public string InputRecord = "";
        public string[] InputTokens = null;
        public int InputTokensLastHeaderIndex = 0;
    }

    public class TrafficMatrixEntry
    {
        public EndpointDetails LocEndpointDetails { get { return new EndpointDetails(); } set { } }
        public EndpointDetails RemEndpointDetails { get { return new EndpointDetails(); } set { } }
        public string InputRecord = null;
        public string[] inputTokens = null;
        public int lastHdrIdx = 0;
        public ulong StatsBytesPerSecTx = 0;
        public ulong StatsBytesPerSecRx = 0;
        public bool IsInterServer = false;
        public void AssignFlow(RateControllerFlow flow) {  }

    }

    public class RateControllerFlow
    {
    }


    public enum NetRateControllerState
    {
        Init,           // All MessageCreateRap have been acked : expecting call to Start().
        SettleTimeout,  // Control loop: settle (dwell) interval delay.
        SettleCallback, // Settle delay expired : awaiting MessageStateZero acks.
        SampleTimeout,  // Control loop: stats sample interval delay.
        UpdateCallback, // Stats sample interval expired : awaiting MessageStateDelta acks.
        Fin,            // Shutdown : awaiting MessageTenantDelete acks.
    }
    public interface INetPolicyModule : IDisposable
    {
        void CallbackSettle();
        void CallbackUpdate();
        void CallbackAlert(MessageNetAlert messageAlert, NetRateControllerState state);
    }

    public class MessageNetAlert
    {
        public uint Length;  // Length of message body excluding this header.
        public uint SeqNo;
        public byte MessageType;
        public ulong AlertVec = 0;
        public ulong EthSrcAddr, EthDestAddr, IPv4SrcAddr, IPv4DestAddr;
        public ulong TenantId, FlowId, i_index, j_index;
        public static ushort SIZEOF_MESSAGE_HEADER =
            4       // Length - ushort proved to be too short. 
            + 4     // SeqNo.
            + 1;    // Message Type.
        public MessageNetAlert() { }
        public MessageNetAlert(uint length, uint seqNo, byte messageType)
        {
            Length = length;
            SeqNo = seqNo;
            MessageType = messageType;
        }
    }



}
