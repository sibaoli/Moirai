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
using OktofsRateControllerNamespace; // for comms to oktofsagent for fast sid lookup.
using System.Net.Sockets;            // for comms to oktofsagent for fast sid lookup.
using System.Threading;

namespace BridgeOktofsAgentNameSpace
{
    /// <summary>
    /// Windows can be very slow performing accountName to SID lookup. In some of our experiments
    /// using TraceIo we were having to allow several tens of seconds for just 12 accounts.
    /// We work around this by exploiting the (acount,sid) cache of an oktofsagent if one is
    /// running on the local host by registering with the oktofsagent as if we were a rate
    /// controller then sending it SID query messages for the accounts we want resolved.
    /// If there is no oktofsagent we fall back on the painfully slow approach.
    /// </summary>
    class BridgeOktofsAgent : IConnectionCallback
    {
        Connection conn = null;
        uint SeqNo = 0;
        uint TenantId = 1;
        ulong ALERT_VEC = 0;
        public delegate int FuncPtrSerialize(byte[] buffer, int offset);
        private AutoResetEvent autoResetEvent = new AutoResetEvent(false);
        int TIMEOUT_MILLISECS = 10000;
        MessageVmNameToStringSidReply messageVmNameToStringSidReply = null;
        const int MAX_INVALID_TID_RETRIES = 32;

        public BridgeOktofsAgent() { }

        public bool Connect()
        {
            string localHost = System.Environment.MachineName;
            int oktofsAgentPort = Parameters.OKTOFSAGENT_TCP_PORT_NUMBER;
            try
            {
                const int locPort = 0;
                conn = new Connection(this, localHost, locPort, oktofsAgentPort);
                return true;
            }
            catch (Exception except)
            {
                const string OKTFSAGENT_NOT_RUNNING =
                    "No connection could be made because the target machine actively refused it";
                if (!except.Message.StartsWith(OKTFSAGENT_NOT_RUNNING))
                    throw;
            }
            return false;
        }

        public bool SendMessageRegister()
        {
            OktoResultCodes resultCode = OktoResultCodes.OKTO_RESULT_TID_NOT_AVAILABLE;
            Random rand = new Random((int)DateTime.Now.Ticks);
            //
            // Deal with collisions on TenantId e.g. if sock shutdown slower than looping new runtime.
            //
            for (int tidInUseCount = 0;
                 tidInUseCount < MAX_INVALID_TID_RETRIES && resultCode == OktoResultCodes.OKTO_RESULT_TID_NOT_AVAILABLE;
                 tidInUseCount++)
            {
                TenantId = (uint)rand.Next(2000, int.MaxValue);
                MessageRegister mRegister = new MessageRegister(++SeqNo, TenantId, ALERT_VEC);
                resultCode = RegisterMessageSupport(conn, mRegister.Serialize, mRegister.SeqNo);
            }
            return (resultCode == OktoResultCodes.OKTO_RESULT_SUCCESS);
        }

        public OktoResultCodes RegisterMessageSupport(Connection conn, FuncPtrSerialize funcPtrSerialize, uint seqNo)
        {
            int countBytesToSend = funcPtrSerialize(conn.sendBuffer, 0);
            int countBytesSent = conn.Send(conn.sendBuffer, 0, countBytesToSend);
            if (countBytesSent != countBytesToSend)
            {
                Console.WriteLine("SendMessageRegister Err: attempt {0} sent {1}.", countBytesToSend, countBytesSent);
                return OktoResultCodes.OKTO_RESULT_SOCK_ERROR;
            }
            int countRecv = conn.Recv(conn.receiveBuffer, 0, (int)MessageAck.TOTAL_MESSAGE_SIZE);
            if (countRecv != MessageAck.TOTAL_MESSAGE_SIZE)
            {
                Console.WriteLine("SendMessageRegister Err: attempt {0} recv {1}.", MessageAck.TOTAL_MESSAGE_SIZE, countRecv);
                return OktoResultCodes.OKTO_RESULT_INVALID_MESSAGE_LENGTH;
            }
            MessageAck ack = MessageAck.CreateFromNetBytes(conn.receiveBuffer, 0);
            //Console.WriteLine("SendSynchronous rcv MessageAck({0},{1})", ack.SeqNo, ack.Result);
            if (ack.SeqNo != seqNo)
            {
                Console.WriteLine("SendMessageRegister Err: SN {0} != expected {1}.", ack.SeqNo, seqNo);
                return OktoResultCodes.OKTO_RESULT_INVALID_MESSAGE;
            }
            else if (ack.Result != (uint)OktoResultCodes.OKTO_RESULT_SUCCESS)
            {
                Console.WriteLine("SendMessageRegister Err: TID {0} not available", TenantId);
            }
            return (OktoResultCodes)ack.Result;
        }

        public string SendMessageSidQuery(string accountName)
        {
            string stringSid = null;
            if (conn == null)
                return null;
            uint sn = ++SeqNo;
            MessageNameToStringSidQuery msg = new MessageNameToStringSidQuery(sn, (uint)accountName.Length, accountName);
            int countBytesToSend = msg.Serialize(conn.sendBuffer, 0);
            int countBytesSent = conn.Send(conn.sendBuffer, 0, countBytesToSend);
            if (countBytesSent != countBytesToSend)
            {
                Console.WriteLine("SendMessageSidQuery Err: attempt {0} sent {1}.", countBytesToSend, countBytesSent);
                return null;
            }
            messageVmNameToStringSidReply = null;
            autoResetEvent.Reset();
            conn.BeginReceive();
            bool timedOut = !autoResetEvent.WaitOne(TIMEOUT_MILLISECS);
            if (timedOut)
                return null;
            if (messageVmNameToStringSidReply.SeqNo != sn)
            {
                Console.WriteLine("qSid seqno mismatch: expected {0} obtained {1}", sn, messageVmNameToStringSidReply.SeqNo);
            }
            else
            {
                //Console.WriteLine("qSid {0}", messageVmNameToStringSidReply.SidString);
                stringSid = messageVmNameToStringSidReply.SidString;
            }

            return stringSid;
        }

        public void ReceiveMessage(Connection conn, MessageTypes messageType, byte[] buff, int offset, int length)
        {
            switch (messageType)
            {
                case MessageTypes.MessageTypeNameToStringSidReply:
                    messageVmNameToStringSidReply
                        = MessageVmNameToStringSidReply.CreateFromNetBytes(conn.receiveBuffer, 0);
                    autoResetEvent.Set();
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public void ReceiveClose(Connection conn) { throw new NotImplementedException(); }

        public void ReceiveError(Connection conn, int errNo)
        {
            string msg = String.Format("BridgeOktofsAgent comms error: conn = {0}, err = {1}", conn.HostName, errNo);
            throw new ApplicationException(msg);
        }

        public void CatchSocketException(Connection conn, SocketException sockEx)
        {
            string msg = String.Format("BridgeOktofsAgent CatchSocketException({0},{1})", conn.HostName, sockEx.Message);
            throw sockEx;
        }

        public void Close()
        {
            if (conn != null)
                conn.Close();
            conn = null;
        }

    }
}
