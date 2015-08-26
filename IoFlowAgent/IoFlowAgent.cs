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
using OktofsRateControllerNamespace;
using System.Threading;
using System.Net.Sockets;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;
using IoFlowNamespace;
using System.Diagnostics;

namespace IoFlowAgentNamespace
{
    /// <summary>
    /// Implements agent-side of control protocol between OktofsRateController and a network agent.
    /// </summary>
    public class IoFlowAgent : IConnectionCallback
    {
        private Connection conn;
        private IIoFlowAgentClient client;
        public delegate int FuncPtrSerialize(byte[] buffer, int offset);
        private uint TenantId = 0;
        Dictionary<uint, IoFlowMessageParams> DictFlowCreateParams = new Dictionary<uint, IoFlowMessageParams>();
        Dictionary<uint, MsgRapArg> DictRapCreateParams = new Dictionary<uint, MsgRapArg>();
        object LockLocal = new object();

        public IoFlowAgent(IIoFlowAgentClient client)
        {
            this.client = client;
            conn = new Connection(this);
        }


        /// <summary>
        /// Send given message on given connection.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message is to be sent.</param>
        /// <param name="funcPtrSerialize">Serilaize() method of the message we want to send.</param>
        /// <returns>True on success, false on error.</returns>
        private bool SendSynchronous(Connection conn, FuncPtrSerialize funcPtrSerialize)
        {
            int countBytesToSend = funcPtrSerialize(conn.sendBuffer, 0);
            int countBytesSent = conn.Send(conn.sendBuffer, 0, countBytesToSend);
            if (countBytesSent != countBytesToSend)
            {
                Console.WriteLine("SendSynchronous Err: attempt {0} sent {1}.", countBytesToSend, countBytesSent);
                return false;
            }
            return true;
        }


        #region IConnectionCallback
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
            switch (messageType)
            {
                case MessageTypes.MessageTypeRegister:
                    {
                        MessageRegister msgRegister = MessageRegister.CreateFromNetBytes(buff, offset);
                        TenantId = msgRegister.TenantId;
                        client.CallbackMessageRegister(conn, msgRegister.TenantId, msgRegister.AlertVec);
                        MessageAck ack = new MessageAck(msgRegister.SeqNo,
                                                        MessageTypes.MessageTypeRegisterAck,
                                                        (uint)OktoResultCodes.OKTO_RESULT_SUCCESS);
                        SendSynchronous(conn, ack.Serialize);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeIoFlowCreate:
                    {
                        MessageIoFlowCreate msgFlowc = MessageIoFlowCreate.CreateFromNetBytes(buff, offset);
                        lock (LockLocal)
                        {
                            foreach (IoFlowMessageParams flowc in msgFlowc.ListParams)
                            {
                                Console.WriteLine("Agent MessageIoFlowCreate({0},{1})", flowc.FlowId, flowc.ParameterString);
                                if (DictFlowCreateParams.ContainsKey(flowc.FlowId))
                                    throw new ArgumentOutOfRangeException(string.Format("Agent flowc dup FlowId {0}", flowc.FlowId));
                                DictFlowCreateParams.Add(flowc.FlowId, flowc);
                            }
                        }
                        MessageAck ack = new MessageAck(msgFlowc.SeqNo,
                                                        MessageTypes.MessageTypeIoFlowCreateAck,
                                                        (uint)OktoResultCodes.OKTO_RESULT_SUCCESS);
                        SendSynchronous(conn, ack.Serialize);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeRapFsCreate:
                    {
                        MessageRapFsCreate msgRapc = MessageRapFsCreate.CreateFromNetBytes(buff, offset);

                        lock (LockLocal)
                        {
                            foreach (MsgRapArg rapc in msgRapc.ListMsgRapArg)
                            {
                                Console.WriteLine("Agent MessageRapFsCreate({0},{1},{2})", rapc.FlowId, rapc.stringSid, rapc.ShareOrVolume);
                                if (!DictFlowCreateParams.ContainsKey(rapc.FlowId))
                                    throw new ArgumentOutOfRangeException(string.Format("Agent rapc invalid FlowId {0}", rapc.FlowId));
                                if (DictRapCreateParams.ContainsKey(rapc.FlowId))
                                    throw new ArgumentOutOfRangeException(string.Format("Agent rapc dup FlowId {0}", rapc.FlowId));
                                if (!DictFlowCreateParams.ContainsKey(rapc.FlowId))
                                    throw new ArgumentOutOfRangeException(string.Format("Agent rapc unmatched FlowId {0}", rapc.FlowId));
                                DictRapCreateParams.Add(rapc.FlowId, rapc);
                            }
                        }
                        //
                        // Params look reasonable and FlowCreate and RapCreate match up.
                        // Now we can invite the client to create its IoFlows.
                        //
                        OktoResultCodes result = client.CallbackIoFlowCreate(DictFlowCreateParams, DictRapCreateParams);

                        MessageAck ack = new MessageAck(msgRapc.SeqNo,
                                                        MessageTypes.MessageTypeRapFsCreateAck,
                                                        (uint)result);
                        SendSynchronous(conn, ack.Serialize);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeStatsZero:
                    {
                        MessageStatsZero msgZero = MessageStatsZero.CreateFromNetBytes(buff, offset);
                        OktoResultCodes result = client.CallbackMessageStatsZero();
                        MessageAck ack = new MessageAck(msgZero.SeqNo,
                                                        MessageTypes.MessageTypeStatsZeroAck,
                                                        (uint)result);
                        SendSynchronous(conn, ack.Serialize);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeIoFlowUpdate:
                    {
                        MessageIoFlowUpdate msgFlowc = MessageIoFlowUpdate.CreateFromNetBytes(buff, offset);
                        lock (LockLocal)
                        {
                            foreach (IoFlowMessageParams flowu in msgFlowc.ListParams)
                                if (!DictFlowCreateParams.ContainsKey(flowu.FlowId))
                                    throw new ArgumentOutOfRangeException(string.Format("Agent flowu invalid FlowId {0}", flowu.FlowId));
                        }
                        OktoResultCodes result = client.CallbackMessageIoFlowUpdate(msgFlowc.ListParams);
                        MessageAck ack = new MessageAck(msgFlowc.SeqNo,
                                                        MessageTypes.MessageTypeIoFlowUpdateAck,
                                                        (uint)result);
                        SendSynchronous(conn, ack.Serialize);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeTenantDelete:
                    {
                        MessageTenantDelete msgTend = MessageTenantDelete.CreateFromNetBytes(buff, offset);
                        OktoResultCodes result = client.CallbackMessageTenantDelete();
                        MessageAck ack = new MessageAck(msgTend.SeqNo,
                                                        MessageTypes.MessageTypeTenantDeleteAck,
                                                        (uint)result);
                        SendSynchronous(conn, ack.Serialize);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeIoFlowStatsQuery:
                    {
                        MessageIoFlowStatsQuery msgStatsQ = MessageIoFlowStatsQuery.CreateFromNetBytes(buff, offset);
                        List<IoFlowMessageParams> listStats = client.CallbackMessageIoFlowStatsQuery();
                        lock (LockLocal)
                        {
                            foreach (IoFlowMessageParams stats in listStats)
                                if (!DictFlowCreateParams.ContainsKey(stats.FlowId))
                                    throw new ArgumentOutOfRangeException(string.Format("Stats reply invalid FlowId {0}", stats.FlowId));
                        }
                        MessageIoFlowStatsReply msgStatsReply = new MessageIoFlowStatsReply(msgStatsQ.SeqNo, listStats, TenantId);
                        SendSynchronous(conn, msgStatsReply.Serialize);
                        conn.BeginReceive();
                        break;
                    }

                case MessageTypes.MessageTypeNameToStringSidBatchQuery:
                    {
                        MessageNameToStringSidBatchQuery msgSidBatchQuery =
                            MessageNameToStringSidBatchQuery.CreateFromNetBytes(buff, offset);
                        Dictionary<string, string> DictVmNameToSid = new Dictionary<string, string>();
                        System.Security.Principal.SecurityIdentifier sid = null;
                        foreach (string vmName in msgSidBatchQuery.ListVmNames)
                        {
                            //XXXET: following two lines are for account names of type europe\
                            sid = VmSID.GetVMnameSid(vmName);
                            if (sid == null)
                            {
                                try
                                {
                                    NTAccount ntaccount = new NTAccount(vmName);
                                    sid = (SecurityIdentifier)ntaccount.Translate(typeof(SecurityIdentifier));
                                }
                                catch (Exception e)
                                {
                                    Debug.Assert(0 == 1, e.Message);
                                }
                            }

                            Console.WriteLine("MessageTypeNameToStringSidBatchQuery: {0} sid {1}", vmName, sid.ToString());
                            DictVmNameToSid.Add(vmName, sid.ToString());
                        }
                        MessageVmNameToStringSidBatchReply msgSidReply =
                            new MessageVmNameToStringSidBatchReply(msgSidBatchQuery.SeqNo,
                                                                   (uint)OktoResultCodes.OKTO_RESULT_SUCCESS,
                                                                   DictVmNameToSid);
                        SendSynchronous(conn, msgSidReply.Serialize);
                        conn.BeginReceive();
                        break;
                    }


                default:
                    {
                        string msg = string.Format("ReceiveMessage: unexpected message type {0}", messageType);
                        throw new ApplicationException(msg);
                    }
            } // switch
        }

        private void Reset()
        {
            lock (LockLocal)
            {
                TenantId = 0;
                DictFlowCreateParams = new Dictionary<uint, IoFlowMessageParams>();
                DictRapCreateParams = new Dictionary<uint, MsgRapArg>();
            }
        }

        /// <summary>
        /// Called by a Connection (TCP to a specific network agent) when TCP has closed the connection.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message arrived.</param>
        public void ReceiveClose(Connection conn)
        {
            Console.WriteLine("IoFlowAgent ReceiveClose: received close (zero bytes) from conn {0}", conn.HostName);
            client.CallbackReceiveClose(conn);
            Reset();
        }

        /// <summary>
        /// Called by a Connection (TCP to a specific network agent) when it encounters a TCP receive error.
        /// </summary>
        /// <param name="conn">Connection (think TCP to specific network agent) on which message arrived.</param>
        public void ReceiveError(Connection conn, int errNo)
        {
            string msg = String.Format("IoFlowAgent TCP comms error: conn = {0}, err = {1}", conn.HostName, errNo);
            client.CallbackReceiveError(conn, errNo);
            Reset();
        }

        /// <summary>
        /// Callback invoked by a connection catches a socket exception.
        /// </summary>
        /// <param name="conn">Connection that caught the socket exception.</param>
        /// <param name="sockEx">The socket exception.</param>
        public void CatchSocketException(Connection conn, SocketException sockEx)
        {
            Console.WriteLine("IoFlowAgent caught SocketException({0},{1})", conn.HostName, sockEx.Message);
            client.CallbackCatchSocketException(sockEx);
            Reset();
        }
        #endregion

    }
}
