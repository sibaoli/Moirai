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
using RateControllerNamespace;

namespace OktofsRateControllerNamespace
{
    /// <summary>
    /// Implementation of the network messages types exchanged between Rate Controller and network agents.
    /// </summary>

    internal interface IMessageSerialize
    {
        int Serialize(byte[] buffer, int offset);
    }

    // These must match the enum _MessageTypes values in oktoagent.h
    public enum MessageTypes
    {
        MessageTypeIllegal,        // Defense against use of uninitialized memory.
        MessageTypeAck,
        MessageTypeRegister,
        MessageTypeRegisterAck,
        MessageTypeFlowCreate,
        MessageTypeFlowCreateAck,
        MessageTypeFlowUpdate,
        MessageTypeFlowUpdateAck,
        MessageTypeStatsZero,
        MessageTypeStatsZeroAck,
        MessageTypeTenantDelete,
        MessageTypeTenantDeleteAck,
        MessageTypeStatsQueryDelta,
        MessageTypeStatsReplyDelta,
        MessageTypeRapFsCreate,
        MessageTypeRapFsCreateAck,
        MessageTypeNameToStringSidQuery,
        MessageTypeNameToStringSidReply,
        MessageTypeAlert,
        MessageTypeIoFlowCreate,
        MessageTypeIoFlowCreateAck,
        MessageTypeIoFlowUpdate,
        MessageTypeIoFlowUpdateAck,
        MessageTypeIoFlowStatsQuery,
        MessageTypeIoFlowStatsReply,
        MessageTypeNameToStringSidBatchQuery,
        MessageTypeNameToStringSidBatchReply,
        EndOfList,                 // Terminator: leave as last in the enumeration. 
    }

    // These must match the #define values in oktouser.h
    public enum OktoResultCodes
    {
        OKTO_RESULT_SUCCESS = 0,
        OKTO_RESULT_EOF = 1,
        OKTO_RESULT_FLOW_NOT_FOUND = 2,
        OKTO_RESULT_RAP_NOT_FOUND = 3,
        OKTO_RESULT_OUT_OF_RESOURCES = 4,
        OKTO_RESULT_FLOW_ALREADY_EXISTS = 5,
        OKTO_RESULT_RAP_ALREADY_EXISTS = 6,
        OKTO_RESULT_INVALID_PARAMETER = 7,
        OKTO_RESULT_NMR_REGISTER_ERROR = 8,
        OKTO_RESULT_PACER_ERR_NEW_FLOW = 9,
        OKTO_RESULT_PACER_ERR_UPDATE_FLOW = 10,
        OKTO_RESULT_PACER_ERR_DELETE_FLOW = 11,
        OKTO_RESULT_INVALID_ADDRESS = 12,
        OKTO_RESULT_COUNT_RAP_NEQ_ZERO = 13,
        OKTO_RESULT_BUFFER_TOO_SMALL = 14,
        OKTO_RESULT_HASH_ADD_FAILURE = 15,
        OKTO_RESULT_TID_CANNOT_CHANGE = 16,      // User-mode only.
        OKTO_RESULT_TID_NOT_AVAILABLE = 17,      // User-mode only.
        OKTO_RESULT_INVALID_MESSAGE_LENGTH = 18, // Only generated in user mode.
        OKTO_RESULT_INVALID_MESSAGE = 19,        // Only generated in user mode.
        OKTO_RESULT_IOCTL_ERROR = 20,            // Only generated in user mode.
        OKTO_RESULT_SOCK_ERROR = 21,             // Only generated in user mode.
        OKTO_RESULT_TENANT_NOT_FOUND = 22,       // Only generated in user mode.
        OKTO_RESULT_ALTER_STATS_SIZE = 23,
        OKTO_RESULT_EXCEEDS_STATS_SIZE = 24,
        OKTO_RESULT_INVALID_COMMAND = 25,
        OKTO_COMM_MSG_INCORRECT_SIZE = 26,
        OKTO_VM_NAME_INVALID_SID = 27,
        OKTO_VM_SID_TOO_LONG = 28,
        OKTO_VM_NAME_TOO_LONG = 29,
        OKTO_SHARE_NAME_TOO_LONG = 30,
        OKTO_RESULT_MESSAGE_NOT_SUPPORTED = 31,   // Only generated in user mode.
        OKTO_FLTMGR_ERROR = 32,
        OKTO_FLTMGR_INSTCTX_NOT_FOUND = 33,
        OKTO_FLUSH_IRPLISTS_FAILED = 34,
        OKTO_DOMAIN_ACCOUNTUSD_NAME_NO_USD = 35,
        OKTO_INVALID_PRIORITY = 36,
        OKTO_INVALID_WEIGHT = 37,
    }

    // These must match the enum of same name in oktofsuser.h
    public enum IoctlReorgFlags
    {
        IoctlFlagsReorgIllegal = 0x00,
        IoctlFlagsReorgFlow = 0x01,
        IoctlFlagsReorgRap = 0x02,
        IoctlFlagsReorgStats = 0x04,
        IoctlFlagsFirstUnused = 0x08,
    }


    public abstract class Message
    {
        protected uint Length;  // Length of message body excluding this header.
        public uint SeqNo;
        protected byte MessageType;
        public static ushort SIZEOF_MESSAGE_HEADER =
            4       // Length - ushort proved to be too short. 
            + 4     // SeqNo.
            + 1;    // Message Type.
        public Message() { }
        public Message(uint length, uint seqNo, byte messageType)
        {
            Length = length;
            SeqNo = seqNo;
            MessageType = messageType;
        }
    }

    public class MessageHeader : Message
    {
        public static uint TOTAL_MESSAGE_SIZE = (uint)(SIZEOF_MESSAGE_HEADER);
        public MessageHeader() { }
        public void InitFromNetBytes(byte[] buffer, int offset)
        {
            Length = (uint)Utils.Int32FromNetBytes(buffer, offset);
            SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset + 4);
            MessageType = buffer[offset + 8];
        }
        public int GetLength() { return (int)Length; }
        public MessageTypes GetMessageType() { return (MessageTypes)MessageType; }
    }

    public class MessageRegister : Message, IMessageSerialize
    {
        private static uint SIZEOF_MESSAGE_BODY = 12;
        public static uint TOTAL_MESSAGE_SIZE = (uint)(SIZEOF_MESSAGE_HEADER + SIZEOF_MESSAGE_BODY);
        public uint TenantId;
        public UInt64 AlertVec;
        public MessageRegister(uint seqNo, uint tenantId, UInt64 alertVec)
            : base(SIZEOF_MESSAGE_BODY, seqNo, (byte)MessageTypes.MessageTypeRegister)
        {
            TenantId = tenantId;
            AlertVec = alertVec;
        }

        public MessageRegister()
            : base(SIZEOF_MESSAGE_BODY, 0, (byte)MessageTypes.MessageTypeRegister)
        {
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)SIZEOF_MESSAGE_BODY, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            byteCount += Utils.Int32ToNetBytes((int)TenantId, buffer, offset + byteCount);
            byteCount += Utils.Int64ToNetBytes((int)AlertVec, buffer, offset + byteCount);
            return (int)TOTAL_MESSAGE_SIZE;
        }

        public static MessageRegister CreateFromNetBytes(byte[] buffer, int offset)
        {
            MessageRegister msg = new MessageRegister();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.TenantId = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.AlertVec = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
            return msg;
        }
    }

    public class MessageAck : Message
    {
        private static uint SIZEOF_MESSAGE_BODY = 5;
        public static uint TOTAL_MESSAGE_SIZE = (uint)(SIZEOF_MESSAGE_HEADER + SIZEOF_MESSAGE_BODY);
        public uint Result;
        public byte SubType; // Disambiguate use of Ack message common to several message pairs.
        public MessageAck()
            : base(SIZEOF_MESSAGE_BODY, 0, (byte)MessageTypes.MessageTypeRegister)
        {
            Result = 0;
        }

        public MessageAck(uint seqNo, MessageTypes subType, uint result)
            : base(SIZEOF_MESSAGE_BODY, seqNo, (byte)MessageTypes.MessageTypeAck)
        {
            SubType = (byte)subType;
            Result = result;
        }

        public static MessageAck CreateFromNetBytes(byte[] buffer, int offset)
        {
            MessageAck msg = new MessageAck();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.Result = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SubType = buffer[offset++];
            return msg;
        }

        public void InitFromNetBytes(byte[] buffer, int offset)
        {
            Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            MessageType = buffer[offset++];
            Result = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            SubType = buffer[offset++];
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)SIZEOF_MESSAGE_BODY, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            byteCount += Utils.Int32ToNetBytes((int)Result, buffer, offset + byteCount);
            buffer[byteCount++] = SubType;
            return (int)TOTAL_MESSAGE_SIZE;
        }
    }

    class MessageFlowCreate : Message, IMessageSerialize
    {
        OktoQueue[] ArrayQosArg;
        public MessageFlowCreate(uint seqNo, OktoQueue[] arrayQosArg)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeFlowCreate)
        {
            ArrayQosArg = arrayQosArg;
            int length = (ArrayQosArg.Length * OktoQueue.SIZEOF_QOS_ARG_CREATE);
            Debug.Assert(length <= int.MaxValue);
            Length = (uint)length;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)Length, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            foreach (OktoQueue qosArg in ArrayQosArg)
                byteCount += qosArg.SerializeCreate(buffer, offset + byteCount);
            Debug.Assert(byteCount == SIZEOF_MESSAGE_HEADER + Length);
            Console.WriteLine("gregos dbg MessageFlowCreate byteCount {0}", byteCount);
            return byteCount;
        }
    }

    class MessageFlowUpdate : Message, IMessageSerialize
    {
        OktoQueue[] ArrayQosArg;
        public MessageFlowUpdate(uint seqNo, OktoQueue[] arrayQosArg)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeFlowUpdate)
        {
            ArrayQosArg = arrayQosArg;
            int length = (ArrayQosArg.Length * OktoQueue.SIZEOF_QOS_ARG);
            Debug.Assert(length <= int.MaxValue);
            Length = (uint)length;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)Length, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            foreach (OktoQueue qosArg in ArrayQosArg)
                byteCount += qosArg.Serialize(buffer, offset + byteCount);
            Debug.Assert(byteCount == SIZEOF_MESSAGE_HEADER + Length);
            return byteCount;
        }
    }

    public class MessageStatsZero : Message, IMessageSerialize
    {
        public MessageStatsZero(uint seqNo)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeStatsZero)
        {
            Length = 0;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes(0, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            return byteCount;
        }

        public static MessageStatsZero CreateFromNetBytes(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            MessageStatsZero msg = new MessageStatsZero(0);
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            return msg;
        }
    }

    public class MessageTenantDelete : Message, IMessageSerialize
    {
        public MessageTenantDelete(uint seqNo)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeTenantDelete)
        {
            Length = 0;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes(0, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            return byteCount;
        }

        public static MessageTenantDelete CreateFromNetBytes(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            MessageTenantDelete msg = new MessageTenantDelete(0);
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            return msg;
        }
    }

    // These must match the enum _StatsQueryFlags values in oktoagent.h
    public enum StatsQueryDeltaFlags
    {
        QueryFlagIllegal = 0x00,        // Defense against use of uninitialized memory.
        QueryFlagTx = 0x01,             // agent to LWF, and RC to agent.
        QueryFlagRx = 0x02,             // agent to LWF, and RC to agent.
        QueryFlagReset = 0x04,          // agent to LWF, and RC to agent.
        QueryFlagBytesToPacer = 0x08,   // agent to LWF only.
        QueryFlagPacketsToPacer = 0x10, // agent to LWF only.
        QueryFlagQueueLength = 0x20,    // RC to agent only.
    }

    class MessageStatsQueryDelta : Message, IMessageSerialize
    {
        private static ushort SIZEOF_MESSAGE_BODY = 12;
        public uint CountRaps;
        public uint DeltaThreshold;    // Threshold as %age, scaled up to 8 bits magnitude and 24 bits fractional.
        public byte Flags = 0;
        public MessageStatsQueryDelta(uint seqNo, uint countRaps, double deltaThreshold, bool reset)
            : base(SIZEOF_MESSAGE_BODY, seqNo, (byte)MessageTypes.MessageTypeStatsQueryDelta)
        {
            CountRaps = countRaps;
            DeltaThreshold = (uint)(deltaThreshold * Math.Pow(2.0, 24.0));
            Flags |= (byte)(reset ? StatsQueryDeltaFlags.QueryFlagReset : 0x00);
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)SIZEOF_MESSAGE_BODY, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            byteCount += Utils.Int32ToNetBytes((int)CountRaps, buffer, offset + byteCount);
            byteCount += Utils.Int32ToNetBytes((int)DeltaThreshold, buffer, offset + byteCount);
            buffer[byteCount++] = Flags;
            byteCount += 3; // skip pad[3].
            return byteCount;
        }
    }

    class MessageStatsReplyDelta : Message
    {
        private const int OKTO_FLT_INFO_BUFF_BYTE_LEN = 256; // ref oktofsuser.h
        public uint Result;
        public ulong IntervalUSecs;       // Time interval over which stats were collected.
        public ushort CountDeviceInfo;
        public ushort CountFlowStats;
        public Dictionary<ushort, FlowStats> dictFlowStats = new Dictionary<ushort, FlowStats>();
        public static ushort SIZEOF_HEADER = SIZEOF_MESSAGE_HEADER;

        public static MessageStatsReplyDelta CreateFromNetBytes(
            byte[] buffer, 
            int offset,
            Connection conn, 
            OktofsRateController rateController)
        {
            //
            // Get header info and lengths of the Device and Stats arrays.
            //
            MessageStatsReplyDelta msg = new MessageStatsReplyDelta();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.Result = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.IntervalUSecs = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
            msg.CountDeviceInfo = (ushort)Utils.Int16FromNetBytes(buffer, offset); offset += 2;
            msg.CountFlowStats = (ushort)Utils.Int16FromNetBytes(buffer, offset); offset += 2;

            //
            // Get the array of Device stats.
            //
            char[] charsToTrim = { '\0' };
            for (int i = 0; i < msg.CountDeviceInfo; i++)
            {
                string deviceName = UnicodeEncoding.Unicode.GetString(buffer, offset, (OKTO_FLT_INFO_BUFF_BYTE_LEN + 2));
                offset += (OKTO_FLT_INFO_BUFF_BYTE_LEN + 2);
                deviceName = deviceName.TrimEnd(charsToTrim);
                Device device = rateController.FindOrCreateDevice(conn.HostName, deviceName);
                lock (device)
                {
                    device.AveInFlightTick.Read.Iops = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveInFlightTick.Read.Tokens = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveInFlightTick.Write.Iops = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveInFlightTick.Write.Tokens = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveInFlightEvent.Read.Iops = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveInFlightEvent.Read.Tokens = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveInFlightEvent.Write.Iops = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveInFlightEvent.Write.Tokens = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveQueueLenTick.Read.Iops = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveQueueLenTick.Read.Tokens = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveQueueLenTick.Write.Iops = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveQueueLenTick.Write.Tokens = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveQueueLenEvent.Read.Iops = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveQueueLenEvent.Read.Tokens = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveQueueLenEvent.Write.Iops = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                    device.AveQueueLenEvent.Write.Tokens = (ulong)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
                }
            }

            //
            // Get the array of FlowStats.
            //
            for (int i = 0; i < msg.CountFlowStats; i++)
            {
                ushort idxRap = (ushort)Utils.Int16FromNetBytes(buffer, offset); offset += 2;
                FlowStats flowStats = FlowStats.CreateFromNetBytes(buffer, offset, out offset);
                msg.dictFlowStats.Add(idxRap, flowStats);
            }

            return msg;
        }
    }

    public class MsgRapArg
    {
        public const uint SIZEOF_MSGRAPARG = 4   // FlowId
                                           + 4   // StrSidLength
                                           + 4   // ShareOrVolumeLength
                                           + 4   // i_index
                                           + 4   // j_index
                                           + Parameters.OKTO_VM_NAME_MAX_CHARS // stingSID
                                           + Parameters.OKTO_SHARE_NAME_MAX_CHARS;
        public uint FlowId;
        public int StrSidLength;
        public int ShareOrVolumeLength;
        public uint i_index;
        public uint j_index;
        public string stringSid;
        public string ShareOrVolume;

        public MsgRapArg() { }

        public static MsgRapArg CreateFromNetBytes(byte[] buffer, int offset)
        {
            MsgRapArg arg = new MsgRapArg();
            arg.FlowId = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            arg.StrSidLength = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            arg.ShareOrVolumeLength = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            arg.i_index = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            arg.j_index = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            arg.stringSid = UTF8Encoding.UTF8.GetString(buffer, offset, Parameters.OKTO_VM_NAME_MAX_CHARS);
            arg.stringSid = arg.stringSid.Substring(0, arg.StrSidLength);
            offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
            arg.ShareOrVolume = UTF8Encoding.UTF8.GetString(buffer, offset, Parameters.OKTO_SHARE_NAME_MAX_CHARS);
            arg.ShareOrVolume = arg.ShareOrVolume.Substring(0, arg.ShareOrVolumeLength);
            offset += Parameters.OKTO_SHARE_NAME_MAX_CHARS;
            return arg;
        }
    }


    public class MessageRapFsCreate : Message, IMessageSerialize
    {
        RAP[] ArrayRapArg;
        public List<MsgRapArg> ListMsgRapArg;

        const int SIZEOF_RAP_FS_ARG = 4   // FlowId
                                    + 4   // VmNameCZLen
                                    + 4   // ShareNameCZLen
                                    + 4   // i_index
                                    + 4   // j_index
                                    + Parameters.OKTO_VM_NAME_MAX_CHARS
                                    + Parameters.OKTO_SHARE_NAME_MAX_CHARS;

        private Encoding Enc = UTF8Encoding.UTF8;

        public MessageRapFsCreate(uint seqNo, RAP[] arrayRapArg)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeRapFsCreate)
        {
            ArrayRapArg = arrayRapArg;
            int length = (ArrayRapArg.Length * SIZEOF_RAP_FS_ARG);
            Debug.Assert(length <= int.MaxValue);
            Length = (uint)length;
        }

        public MessageRapFsCreate()
            : base(0, 0, (byte)MessageTypes.MessageTypeRapFsCreate)
        { }


        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)Length, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            foreach (RAP rapArg in ArrayRapArg)
                byteCount += SerializeRapFsArg(rapArg, buffer, offset + byteCount,true);
            Debug.Assert(byteCount == SIZEOF_MESSAGE_HEADER + Length);
            Console.WriteLine("gregos dbg MessageRapFsCreate byteCount {0}", byteCount);
            return byteCount;
        }

        public int SerializeIoFlow(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)Length, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            foreach (RAP rapArg in ArrayRapArg)
                byteCount += SerializeRapFsArg(rapArg, buffer, offset + byteCount, false);
            Debug.Assert(byteCount == SIZEOF_MESSAGE_HEADER + Length);
            Console.WriteLine("gregos dbg MessageRapFsCreate byteCount {0}", byteCount);
            return byteCount;
        }

        private int SerializeRapFsArg(RAP rap, byte[] buffer, int offset, bool isOktofs)
        {
            int startOffset = offset;
            string stringSid = rap.LocEndpointDetails.StringSid;
            Utils.Int32ToNetBytes((int)rap.Queue.FlowId, buffer, offset); offset += 4; // FlowId.
            Utils.Int32ToNetBytes((int)stringSid.Length, buffer, offset); offset += 4; // StringSid
            int shareLen = (isOktofs ? (int)rap.ShareOrVolume.Length : (int)rap.LocEndpointDetails.FileName.Length);
            Utils.Int32ToNetBytes(shareLen, buffer, offset); offset += 4; // ShareNameCZLen
            Utils.Int32ToNetBytes(rap.i_index, buffer, offset); offset += 4; // i_index
            Utils.Int32ToNetBytes(rap.j_index, buffer, offset); offset += 4; // j_index
            for (int i = 0; i < Parameters.OKTO_VM_NAME_MAX_CHARS; i++)
                buffer[offset + i] = 0;  // ensure chars zero terminated in buffer.
            int encByteCount = Enc.GetBytes(stringSid, 0, stringSid.Length, buffer, offset);
            if (encByteCount != stringSid.Length || encByteCount > Parameters.OKTO_VM_NAME_MAX_CHARS)
                throw new ApplicationException(String.Format("SerializeRapFsArg VM enc({0}) overrun {1}.", encByteCount, rap.LocEndpointDetails.StringSid));
            offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
            for (int i = 0; i < Parameters.OKTO_SHARE_NAME_MAX_CHARS; i++)
                buffer[offset + i] = 0;  // ensure chars zero terminated in buffer.
            string strShare = (isOktofs ? rap.ShareOrVolume : rap.LocEndpointDetails.FileName);
            encByteCount = Enc.GetBytes(strShare, 0, shareLen, buffer, offset);
            if (encByteCount != shareLen || encByteCount > Parameters.OKTO_SHARE_NAME_MAX_CHARS)
            {
                Console.WriteLine("SerializeRapFsArg err serializing {0}", rap.ShareOrVolume);
                throw new ApplicationException(String.Format("SerializeRapFsArg SHARE enc({0}) overrun {1}.", encByteCount, rap.ShareOrVolume.Length));
            }
            offset += Parameters.OKTO_SHARE_NAME_MAX_CHARS;
            Debug.Assert(offset - startOffset == SIZEOF_RAP_FS_ARG);
            return SIZEOF_RAP_FS_ARG;
        }


        public static MessageRapFsCreate CreateFromNetBytes(byte[] buffer, int offset)
        {
            MessageRapFsCreate msg = new MessageRapFsCreate();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.ListMsgRapArg = new List<MsgRapArg>();
            uint bytesRemaining = msg.Length;
            while (bytesRemaining >= MsgRapArg.SIZEOF_MSGRAPARG)
            {
                MsgRapArg arg = MsgRapArg.CreateFromNetBytes(buffer, offset);
                msg.ListMsgRapArg.Add(arg);
                bytesRemaining -= (int)MsgRapArg.SIZEOF_MSGRAPARG;
                offset += (int)MsgRapArg.SIZEOF_MSGRAPARG;
            }
            return msg;
        }

    }

    public class MessageNameToStringSidQuery : Message, IMessageSerialize
    {
        const int SIZEOF_NAME_TO_SID_QUERY = 4   // VmNameCZLen
            + Parameters.OKTO_VM_NAME_MAX_CHARS; // VmNameBuff
        public uint VmNameCzLen;
        public string VmName;

        private Encoding Enc = UTF8Encoding.UTF8;

        public MessageNameToStringSidQuery(uint seqNo, uint vmNameCzLen, string vmName)
            : base(SIZEOF_NAME_TO_SID_QUERY, seqNo, (byte)MessageTypes.MessageTypeNameToStringSidQuery)
        {
            if (String.IsNullOrEmpty(vmName) || String.IsNullOrWhiteSpace(vmName) || vmName.Length > Parameters.OKTO_VM_NAME_MAX_CHARS)
                throw new ArgumentOutOfRangeException("Error: Invalid vmName.");
            VmNameCzLen = (uint)vmName.Length;
            VmName = vmName;
        }

        public MessageNameToStringSidQuery()
            : base(SIZEOF_NAME_TO_SID_QUERY, 0, (byte)MessageTypes.MessageTypeNameToStringSidQuery)
        {  }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)SIZEOF_NAME_TO_SID_QUERY, buffer, offset); offset += 4;
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset); offset += 4;
            buffer[offset++] = MessageType;
            byteCount++;
            byteCount += Utils.Int32ToNetBytes((int)VmNameCzLen, buffer, offset); offset += 4;
            for (int i = 0; i < Parameters.OKTO_VM_NAME_MAX_CHARS; i++)
                buffer[offset + i] = 0;  // ensure chars zero terminated in buffer.
            int encByteCount = Enc.GetBytes(VmName, 0, VmName.Length, buffer, offset);
            if (encByteCount != VmName.Length || encByteCount > Parameters.OKTO_VM_NAME_MAX_CHARS)
                throw new ApplicationException(String.Format("SerializeSidQuery enc({0}) overrun {1}.", VmName));
            byteCount += Parameters.OKTO_VM_NAME_MAX_CHARS;
            offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
            return byteCount;
        }

        public static MessageNameToStringSidQuery CreateFromNetBytes(byte[] buffer, int offset)
        {
            MessageNameToStringSidQuery msg = new MessageNameToStringSidQuery();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.VmNameCzLen = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            Encoding Enc = UTF8Encoding.UTF8;
            msg.VmName = Enc.GetString(buffer, offset, Parameters.OKTO_VM_NAME_MAX_CHARS);
            msg.VmName = msg.VmName.Substring(0, (int)msg.VmNameCzLen);
            offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
            return msg;
        }
    }

    public class MessageVmNameToStringSidReply : Message
    {
        private static uint SIZEOF_MESSAGE_BODY = 4  // Result
                                                + 4  // VmNameCzLen
                                                + Parameters.OKTO_VM_NAME_MAX_CHARS
                                                + 4  // SidStringCZLen
                                                + Parameters.OKTO_SID_BUFF_BYTE_LEN;
        public uint Result;
        public uint VmNameCzLen;
        public string VmName;
        public uint SidStringCZLen;
        public string SidString;

        public static ushort SIZEOF_HEADER = SIZEOF_MESSAGE_HEADER;
        private Encoding Enc = UTF8Encoding.UTF8;
        
        public MessageVmNameToStringSidReply(
            uint seqNo, 
            uint vmNameCzLen, 
            string vmName,
            uint sidStringCZLen,
            string sidString)
            : base(SIZEOF_MESSAGE_BODY, seqNo, (byte)MessageTypes.MessageTypeNameToStringSidReply)
        {
            if (String.IsNullOrEmpty(vmName) || String.IsNullOrWhiteSpace(vmName) || vmName.Length > Parameters.OKTO_VM_NAME_MAX_CHARS)
                throw new ArgumentOutOfRangeException("Error: Invalid vmName.");
            if (String.IsNullOrEmpty(sidString) || String.IsNullOrWhiteSpace(sidString) || vmName.Length > Parameters.OKTO_SID_BUFF_BYTE_LEN)
                throw new ArgumentOutOfRangeException("Error: Invalid vmName.");
            VmNameCzLen = (uint)vmName.Length;
            VmName = vmName;
            SidStringCZLen = sidStringCZLen;
            SidString = sidString;
        }

        public MessageVmNameToStringSidReply()
            : base(SIZEOF_MESSAGE_BODY, 0, (byte)MessageTypes.MessageTypeNameToStringSidReply)
        { }

        public static MessageVmNameToStringSidReply CreateFromNetBytes(byte[] buffer, int offset)
        {
            MessageVmNameToStringSidReply msg = new MessageVmNameToStringSidReply();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.Result = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.VmNameCzLen = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.VmName = UTF8Encoding.UTF8.GetString(buffer, offset, (int)msg.VmNameCzLen);
            Debug.Assert(msg.VmNameCzLen == msg.VmName.Length);
            offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
            msg.SidStringCZLen = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SidString = UTF8Encoding.UTF8.GetString(buffer, offset, (int)msg.SidStringCZLen);
            Debug.Assert(msg.SidStringCZLen == msg.SidString.Length);
            offset += Parameters.OKTO_SID_BUFF_BYTE_LEN;
            return msg;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)SIZEOF_MESSAGE_BODY, buffer, offset); offset += 4;
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset); offset += 4;
            buffer[offset++] = MessageType;
            byteCount++;
            byteCount += Utils.Int32ToNetBytes((int)Result, buffer, offset); offset += 4;
            byteCount += Utils.Int32ToNetBytes((int)VmNameCzLen, buffer, offset); offset += 4;
            for (int i = 0; i < Parameters.OKTO_VM_NAME_MAX_CHARS; i++)
                buffer[offset + i] = 0;  // ensure chars zero terminated in buffer.
            int encByteCount = Enc.GetBytes(VmName, 0, VmName.Length, buffer, offset);
            if (encByteCount != VmName.Length || encByteCount > Parameters.OKTO_VM_NAME_MAX_CHARS)
                throw new ApplicationException(String.Format("SerializeSidReply VM enc({0}) overrun {1}.", VmName));
            byteCount += Parameters.OKTO_VM_NAME_MAX_CHARS;
            offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
            byteCount += Utils.Int32ToNetBytes((int)SidStringCZLen, buffer, offset); offset += 4;
            for (int i = 0; i < Parameters.OKTO_SID_BUFF_BYTE_LEN; i++)
                buffer[offset + i] = 0;  // ensure chars zero terminated in buffer.
            encByteCount = Enc.GetBytes(SidString, 0, SidString.Length, buffer, offset);
            if (encByteCount != SidString.Length || encByteCount > Parameters.OKTO_SID_BUFF_BYTE_LEN)
                throw new ApplicationException(String.Format("SerializeSidReply SID enc({0}) overrun {1}.", SidString));
            byteCount += Parameters.OKTO_SID_BUFF_BYTE_LEN;
            offset += Parameters.OKTO_SID_BUFF_BYTE_LEN;
            return byteCount;
        }
    }

    public class MessageAlert : Message, IMessageSerialize
    {
        private OktoAlertType alertType;
        private ulong[] args;
        private uint seqNo = 0;
        private ulong netAlertVec = 0;

        public OktoAlertType AlertType { get { return alertType; } set { } }
        public ulong[] Args { get { return args; } set { } }
        public ulong NetAlertVec { get { return netAlertVec; } }

        public MessageAlert(OktoAlertType alertType, ulong[] args)
            : base(0, 0, (byte)MessageTypes.MessageTypeAlert)
        {
            this.alertType = alertType;
            this.args = args;
            // Message body is <AlertVec,argsLen,UInt64[argsLen]>.
            Length = (uint) ( 8 + 4 + (args.Length * 8));
        }

        public MessageAlert()
            : base(0, 0, (byte)MessageTypes.MessageTypeAlert)
        {
            this.alertType = OktoAlertType.AlertUnused;
            Length = 0;
        }

        public uint CountBytesNeeded()
        {
            return SIZEOF_MESSAGE_HEADER + Length;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes((int)Length, buffer, offset); offset += 4;
            byteCount += Utils.Int32ToNetBytes((int)seqNo++, buffer, offset); offset += 4;
            buffer[offset++] = MessageType;
            byteCount++;

            byteCount += Utils.Int64ToNetBytes((int)alertType, buffer, offset); offset += 8;
            byteCount += Utils.Int32ToNetBytes((int)args.Length, buffer, offset); offset += 4;
            for (int i = 0; i < args.Length; i++)
            {
                byteCount += Utils.Int64ToNetBytes((long)args[i], buffer, offset);
                offset += 8;
            }
            return byteCount;
        }

        public static MessageAlert CreateFromNetBytes(byte[] buffer, int offset)
        {
            MessageAlert msg = new MessageAlert();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            // Message body is <AlertVec,argsLen,UInt64[argsLen]>.
            msg.alertType = (OktoAlertType)Utils.Int64FromNetBytes(buffer, offset); offset += 8;
            int argsLen = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.args = new ulong[argsLen];
            for (int i = 0; i < argsLen; i++)
            {
                msg.args[i] = (ulong)Utils.Int64FromNetBytes(buffer, offset);
                offset += 8;
            }
            return msg;
        }

        public static MessageAlert CreateFromNetAlert(MessageNetAlert netAlert)
        {
            MessageAlert msg = new MessageAlert();
            msg.Length = netAlert.Length;
            msg.SeqNo = netAlert.SeqNo;
            msg.MessageType = netAlert.MessageType;
            msg.netAlertVec= netAlert.AlertVec;
            msg.args = new ulong[8];
            msg.args[0] = netAlert.EthSrcAddr;
            msg.args[1] = netAlert.EthDestAddr;
            msg.args[2] = netAlert.IPv4SrcAddr;
            msg.args[3] = netAlert.IPv4DestAddr;
            msg.args[4] = netAlert.TenantId;
            msg.args[5] = netAlert.FlowId;
            msg.args[6] = netAlert.i_index;
            msg.args[7] = netAlert.j_index;
            return msg;
        }
    }

    public class IoFlowMessageParams
    {
        public uint flowId;
        public byte flags;
        public string parameterString;
        public int netByteCount;

        public uint FlowId {get{ return flowId; }}
        public byte Flags { get { return flags; } }
        public string ParameterString { get { return parameterString; } }
        public int NetByteCount { get { return netByteCount; } }

        public IoFlowMessageParams(uint flowId, string parameterString, byte flags)
        {
            this.flowId = flowId;
            this.parameterString = parameterString;
            this.flags = flags;
        }

        public static int GetNetByteCount(string parameterString)
        {
            int paramsLength = 4   // FlowId
                             + 4   // UTF8 encoding length
                             + UTF8Encoding.UTF8.GetByteCount(parameterString)
                             + 1;  // Flags;
            return paramsLength;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            int encLength = UTF8Encoding.UTF8.GetByteCount(ParameterString);
            offset += Utils.Int32ToNetBytes((int)FlowId, buffer, offset);
            offset += Utils.Int32ToNetBytes(encLength, buffer, offset);
            int encByteCount = UTF8Encoding.UTF8.GetBytes(ParameterString, 0, ParameterString.Length, buffer, offset);
            if (encByteCount != encLength)
                throw new ApplicationException(String.Format("Serialize IoFlowParams FlowId {0} params {1}.", FlowId, ParameterString));
            offset += encByteCount;
            buffer[offset++] = Flags;
            int byteCount = offset - oldOffset;
            return byteCount;
        }

        public static IoFlowMessageParams CreateFromNetBytes(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            IoFlowMessageParams msg = new IoFlowMessageParams(0,null,0);
            msg.flowId = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            int paramsLength = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.parameterString = UTF8Encoding.UTF8.GetString(buffer, offset, paramsLength);
            offset += paramsLength;
            Debug.Assert(paramsLength == msg.ParameterString.Length);
            msg.flags = buffer[offset++];
            msg.netByteCount = offset - oldOffset;
            return msg;
        }
    }

    public class MessageIoFlowCreate : Message, IMessageSerialize
    {
        List<Flow> ListIoFlow;
        public List<IoFlowMessageParams> ListParams;

        public MessageIoFlowCreate(uint seqNo, List<Flow> listIoFlow)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeIoFlowCreate)
        {
            ListIoFlow = listIoFlow;
            int length = 4; // msg.countParams.
            foreach (Flow flow in listIoFlow)
                length += IoFlowMessageParams.GetNetByteCount(flow.InputRecord);
            Debug.Assert(length <= int.MaxValue);
            Length = (uint)length;
        }

        public MessageIoFlowCreate()
            : base(0, 0, (byte)MessageTypes.MessageTypeIoFlowCreate)
        { }

        public int Serialize(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            offset += Utils.Int32ToNetBytes((int)Length, buffer, offset);
            offset += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset);
            buffer[offset++] = MessageType;
            offset += Utils.Int32ToNetBytes((int)ListIoFlow.Count, buffer, offset);
            foreach (Flow flow in ListIoFlow)
            {
                IoFlowMessageParams ioFlowParams = new IoFlowMessageParams(flow.FlowId,flow.InputRecord,flow.Flags);
                offset += ioFlowParams.Serialize(buffer, offset);
            }
            int byteCount = offset - oldOffset;
            return byteCount;
        }

        public static MessageIoFlowCreate CreateFromNetBytes(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            MessageIoFlowCreate msg = new MessageIoFlowCreate();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.ListParams = new List<IoFlowMessageParams>();
            int countParams = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            for (int i = 0; i < countParams; i++)
            {
                IoFlowMessageParams ioFlowParams = IoFlowMessageParams.CreateFromNetBytes(buffer, offset);
                msg.ListParams.Add(ioFlowParams);
                offset += ioFlowParams.NetByteCount;
            }
            return msg;
        }
    }

    public class MessageIoFlowUpdate : Message, IMessageSerialize
    {
        List<Flow> ListIoFlow;
        public List<IoFlowMessageParams> ListParams;

        public MessageIoFlowUpdate(uint seqNo, List<Flow> listIoFlow)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeIoFlowUpdate)
        {
            ListIoFlow = listIoFlow;
            int length = 4; // msg.countParams.
            foreach (Flow flow in listIoFlow)
                length += IoFlowMessageParams.GetNetByteCount(flow.IoFlowUpdateParams);
            Length = (uint)length;
        }

        public MessageIoFlowUpdate()
            : base(0, 0, (byte)MessageTypes.MessageTypeIoFlowUpdate)
        { }

        public int Serialize(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            offset += Utils.Int32ToNetBytes((int)Length, buffer, offset);
            offset += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset);
            buffer[offset++] = MessageType;
            offset += Utils.Int32ToNetBytes((int)ListIoFlow.Count, buffer, offset);
            foreach (Flow flow in ListIoFlow)
            {
                IoFlowMessageParams ioFlowParams = new IoFlowMessageParams(flow.FlowId, flow.IoFlowUpdateParams, flow.Flags);
                offset += ioFlowParams.Serialize(buffer, offset);
            }
            int byteCount = offset - oldOffset;
            return byteCount;
        }

        public static MessageIoFlowUpdate CreateFromNetBytes(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            MessageIoFlowUpdate msg = new MessageIoFlowUpdate();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.ListParams = new List<IoFlowMessageParams>();
            int countParams = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            for (int i = 0; i < countParams; i++)
            {
                IoFlowMessageParams ioFlowParams = IoFlowMessageParams.CreateFromNetBytes(buffer, offset);
                msg.ListParams.Add(ioFlowParams);
                offset += ioFlowParams.NetByteCount;
            }
            return msg;
        }
    }

    public class MessageIoFlowStatsQuery : Message, IMessageSerialize
    {
        public MessageIoFlowStatsQuery(uint seqNo)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeIoFlowStatsQuery)
        {
            Length = 0;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int byteCount = 0;
            byteCount += Utils.Int32ToNetBytes(0, buffer, offset);
            byteCount += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset + byteCount);
            buffer[offset + (byteCount++)] = MessageType;
            return byteCount;
        }

        public static MessageIoFlowStatsQuery CreateFromNetBytes(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            MessageIoFlowStatsQuery msg = new MessageIoFlowStatsQuery(0);
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            return msg;
        }
    }

    public class MessageIoFlowStatsReply : Message, IMessageSerialize
    {
        public List<IoFlowMessageParams> ListParams;
        public uint TenantId = 0;

        public MessageIoFlowStatsReply(uint seqNo, List<IoFlowMessageParams> listParams, uint tenantId)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeIoFlowStatsReply)
        {
            ListParams = listParams;
            TenantId = tenantId;
            int length = 4 + 4; // len(msg.countParam+msg.TenantId); 
            foreach (IoFlowMessageParams stats in listParams)
                length += IoFlowMessageParams.GetNetByteCount(stats.ParameterString);
            Length = (uint)length;
        }

        public MessageIoFlowStatsReply()
            : base(0, 0, (byte)MessageTypes.MessageTypeIoFlowStatsReply)
        { }

        public int Serialize(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            offset += Utils.Int32ToNetBytes((int)Length, buffer, offset);
            offset += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset);
            buffer[offset++] = MessageType;
            offset += Utils.Int32ToNetBytes((int)TenantId, buffer, offset);
            offset += Utils.Int32ToNetBytes((int)ListParams.Count, buffer, offset);
            foreach (IoFlowMessageParams stats in ListParams)
                offset += stats.Serialize(buffer, offset);
            int byteCount = offset - oldOffset;
            return byteCount;
        }

        public static MessageIoFlowStatsReply CreateFromNetBytes(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            MessageIoFlowStatsReply msg = new MessageIoFlowStatsReply();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.TenantId = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.ListParams = new List<IoFlowMessageParams>();
            int countParams = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            for (int i = 0; i < countParams; i++)
            {
                IoFlowMessageParams ioFlowParams = IoFlowMessageParams.CreateFromNetBytes(buffer, offset);
                msg.ListParams.Add(ioFlowParams);
                offset += ioFlowParams.NetByteCount;
            }
            return msg;
        }
    }

    public class MessageNameToStringSidBatchQuery : Message, IMessageSerialize
    {
        public List<string> ListVmNames = new List<string>();

        public static int GetNetByteCount(int countVmNames)
        {
            int netByteCount = 4   // countVmNames
                             + (4 * countVmNames) // length of VmName[i]
                             + (Parameters.OKTO_VM_NAME_MAX_CHARS * countVmNames);  // VmName[i].
            return netByteCount;
        }

        public MessageNameToStringSidBatchQuery(uint seqNo, List<string> listVmNames)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeNameToStringSidBatchQuery)
        {
            foreach (string vmName in listVmNames)
                if (String.IsNullOrEmpty(vmName) || String.IsNullOrWhiteSpace(vmName) || vmName.Length > Parameters.OKTO_VM_NAME_MAX_CHARS)
                    throw new ArgumentOutOfRangeException(string.Format("Error: Invalid vmName {0}",vmName));
            ListVmNames = listVmNames;
            Length = (uint)MessageNameToStringSidBatchQuery.GetNetByteCount(ListVmNames.Count);
        }

        public MessageNameToStringSidBatchQuery()
            : base(0, 0, (byte)MessageTypes.MessageTypeNameToStringSidBatchQuery)
        { }

        public int Serialize(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            int msgLength = GetNetByteCount(ListVmNames.Count);
            offset += Utils.Int32ToNetBytes(msgLength, buffer, offset);
            offset += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset);
            buffer[offset++] = MessageType;
            offset += Utils.Int32ToNetBytes(ListVmNames.Count, buffer, offset);
            foreach (string vmName in ListVmNames)
            {
                offset += Utils.Int32ToNetBytes(vmName.Length, buffer, offset);
                for (int i = 0; i < Parameters.OKTO_VM_NAME_MAX_CHARS; i++)
                    buffer[offset + i] = 0;  // ensure chars zero terminated in buffer.
                int encByteCount = UTF8Encoding.UTF8.GetBytes(vmName, 0, vmName.Length, buffer, offset);
                if (encByteCount != vmName.Length || encByteCount > Parameters.OKTO_VM_NAME_MAX_CHARS)
                    throw new ApplicationException(String.Format("SerializeSidQuery enc({0}) overrun {1}.", vmName));
                offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
            }
            int byteCount = offset - oldOffset;
            return byteCount;
        }

        public static MessageNameToStringSidBatchQuery CreateFromNetBytes(byte[] buffer, int offset)
        {
            MessageNameToStringSidBatchQuery msg = new MessageNameToStringSidBatchQuery();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            int CountVmNames = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            for (int i = 0; i < CountVmNames; i++)
            {
                int vmNameLength = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
                string vmName = UTF8Encoding.UTF8.GetString(buffer, offset, Parameters.OKTO_VM_NAME_MAX_CHARS);
                vmName = vmName.Substring(0, vmNameLength);
                offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
                msg.ListVmNames.Add(vmName);
            }
            return msg;
        }
    }

    public class MessageVmNameToStringSidBatchReply : Message
    {
        public uint Result;
        public Dictionary<string, string> DictVmNameToSid = new Dictionary<string, string>();

        public static int GetNetByteCount(int countEntries)
        {
            int netByteCount = 4   // Result
                             + 4   // count (VmName,Sid) pairs
                             + (4 * countEntries) // int : length of VmName[i]
                             + (Parameters.OKTO_VM_NAME_MAX_CHARS * countEntries)  // VmName[i].
                             + (4 * countEntries) // int : length of Sid[i]
                             + (Parameters.OKTO_SID_BUFF_BYTE_LEN * countEntries);  // Sid[i].
            return netByteCount;
        }

        public MessageVmNameToStringSidBatchReply(
            uint seqNo,
            uint result,
            Dictionary<string,string> dictVmNameToSid)
            : base(0, seqNo, (byte)MessageTypes.MessageTypeNameToStringSidBatchReply)
        {
            if (dictVmNameToSid.Count == 0)
                throw new ArgumentOutOfRangeException(string.Format("Error: sid count zero."));
            foreach (string vmName in dictVmNameToSid.Keys)
                if (String.IsNullOrEmpty(vmName) || String.IsNullOrWhiteSpace(vmName) || vmName.Length > Parameters.OKTO_VM_NAME_MAX_CHARS)
                    throw new ArgumentOutOfRangeException("Error: Invalid vmName.");
            foreach (string sidString in dictVmNameToSid.Values)
                if (String.IsNullOrEmpty(sidString) || String.IsNullOrWhiteSpace(sidString) || sidString.Length > Parameters.OKTO_SID_BUFF_BYTE_LEN)
                    throw new ArgumentOutOfRangeException("Error: Invalid sidString.");
            Result = result;
            DictVmNameToSid = dictVmNameToSid;
            Length = (uint)MessageVmNameToStringSidBatchReply.GetNetByteCount(DictVmNameToSid.Count);
        }

        public MessageVmNameToStringSidBatchReply()
            : base(0, 0, (byte)MessageTypes.MessageTypeNameToStringSidBatchReply)
        { }

        public static MessageVmNameToStringSidBatchReply CreateFromNetBytes(byte[] buffer, int offset)
        {
            MessageVmNameToStringSidBatchReply msg = new MessageVmNameToStringSidBatchReply();
            msg.Length = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.SeqNo = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            msg.MessageType = buffer[offset++];
            msg.Result = (uint)Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            int CountSids = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
            for (int i = 0; i < CountSids; i++)
            {
                int VmNameCzLen = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
                string VmName = UTF8Encoding.UTF8.GetString(buffer, offset, VmNameCzLen);
                offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
                int SidStringCZLen = Utils.Int32FromNetBytes(buffer, offset); offset += 4;
                string SidString = UTF8Encoding.UTF8.GetString(buffer, offset, SidStringCZLen);
                offset += Parameters.OKTO_SID_BUFF_BYTE_LEN;
                msg.DictVmNameToSid.Add(VmName,SidString);
            }
            return msg;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int oldOffset = offset;
            int msgLength = GetNetByteCount(DictVmNameToSid.Count);
            offset += Utils.Int32ToNetBytes(msgLength, buffer, offset);
            offset += Utils.Int32ToNetBytes((int)SeqNo, buffer, offset);
            buffer[offset++] = MessageType;
            offset += Utils.Int32ToNetBytes((int)Result, buffer, offset);
            offset += Utils.Int32ToNetBytes((int)DictVmNameToSid.Count, buffer, offset);
            foreach (KeyValuePair<string,string> kvp in DictVmNameToSid)
            {
                offset += Utils.Int32ToNetBytes(kvp.Key.Length, buffer, offset);
                for (int i = 0; i < Parameters.OKTO_VM_NAME_MAX_CHARS; i++)
                    buffer[offset + i] = 0;  // ensure chars zero terminated in buffer.
                int encByteCount = UTF8Encoding.UTF8.GetBytes(kvp.Key, 0, kvp.Key.Length, buffer, offset);
                if (encByteCount > Parameters.OKTO_VM_NAME_MAX_CHARS)
                    throw new ApplicationException(String.Format("SerializeSidBatchReply VM enc({0}) overrun {1}.", kvp.Key));
                offset += Parameters.OKTO_VM_NAME_MAX_CHARS;
                offset += Utils.Int32ToNetBytes(kvp.Value.Length, buffer, offset);
                for (int i = 0; i < Parameters.OKTO_SID_BUFF_BYTE_LEN; i++)
                    buffer[offset + i] = 0;  // ensure chars zero terminated in buffer.
                encByteCount = UTF8Encoding.UTF8.GetBytes(kvp.Value, 0, kvp.Value.Length, buffer, offset);
                if (encByteCount > Parameters.OKTO_SID_BUFF_BYTE_LEN)
                    throw new ApplicationException(String.Format("SerializeSidBatchReply SID enc({0}) overrun {1}.", kvp.Value));
                offset += Parameters.OKTO_SID_BUFF_BYTE_LEN;
            }
            int byteCount = offset - oldOffset;
            return byteCount;
        }
    }



}
