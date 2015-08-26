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

namespace OktofsRateControllerNamespace
{
    // These must match the enum _OKTO_FLOW_FLAGS in oktouser.h for both storage and network stacks
    public enum OktoFlowFlags : byte
    {
        FlowFlagDeleting   = 0x01,   // Set iff delete in progress against this flow.
        FlowFlagIsAtC      = 0x02,   // Is at C (on Hyper-V server).
        FlowFlagIsAtH      = 0x04,   // Is at H (on storage server).
        FlowFlagNoFastpath = 0x08,   // Force IRPs through queue/dequeue path even if compliant.
        FlowFlagNoStats    = 0x10,   // IRPs may be queued but no stats are collected.
        FlowFlagTwoStage   = 0x20,   // Flow exists at C and H - IRP queued max once at C xor H.
        FlowFlagActive     = 0x40,   // Set iff Rate Controller thinks active (NDIS LWF only).
        FlowFlagInvalid    = 0x80,   // Flags are invalid if set GE this value.
    }

    /// <summary>
    /// Represents a rate limiter queue on which token rates and cost
    /// functions can be specified.
    /// </summary>
    public class OktoQueue
    {
        uint flowId;
        Connection conn;
        public List<RAP> listRap;  // Ordered on matrix (i,j) during construction.
        public const int SIZEOF_QOS_ARG = // size and wire format when serialized into a message
            4    // FlowId
            + (8 * Parameters.OKTO_MAX_VEC_LEN * 2);  // pairs (tokenRatePerSecond,bucketCapacity)
        public const int SIZEOF_QOS_ARG_CREATE = // size and wire format when serialized into a message
            SIZEOF_QOS_ARG                   // as above
            + CostFuncVec.SIZEOF_COST_FUNC_VEC  // CostFuncRead
            + CostFuncVec.SIZEOF_COST_FUNC_VEC // CostFuncWrite
            + 1  // vecLen
            + 1  // priority
            + 1; // flags

        private ulong[] tokSecVec = new ulong[Parameters.OKTO_MAX_VEC_LEN];
        private ulong[] bucketCapacity = new ulong[Parameters.OKTO_MAX_VEC_LEN];
        private CostFuncVec costFuncVecRead = null;
        private CostFuncVec costFuncVecWrite = null;
        public uint FlowId { get { return flowId; } }

        public ulong[] TokSecVec
        {
            get { return tokSecVec; }
            set
            {
                if (value.Length != Parameters.OKTO_MAX_VEC_LEN)
                    throw new ArgumentException("num elements not equal to Parameters.OKTO_MAX_VEC_LEN");
                tokSecVec = value;
                bucketCapacity = value;
                isChanged = true;
             }
        }

        public ulong[] BucketCapacity
        {
            get { return bucketCapacity; }
            set
            {
                if (value.Length != Parameters.OKTO_MAX_VEC_LEN)
                    throw new ArgumentException("num elements not equal to Parameters.OKTO_MAX_VEC_LEN");
                bucketCapacity = value;
                isChanged = true;
            }
        }

        public CostFuncVec CostFuncVecRead { get { return costFuncVecRead; } }
        public CostFuncVec CostFuncVecWrite { get { return costFuncVecWrite; } }

        private byte vecLen = Parameters.OKTO_MAX_VEC_LEN;
        public ulong VecLen
        {
            get { return vecLen; }
            set
            {
                if (value < 1 && value > Parameters.OKTO_MAX_VEC_LEN)
                    throw new ArgumentOutOfRangeException("vecLen");
                vecLen = (byte)value;
                isChanged = true;
             }
        }

        byte flags = 0;
        public byte Flags
        {
            get { return flags; }
        }

        byte priority;
        public byte Priority
        {
            get { return priority; }
            set
            {
                if (value < 0 || value > 7)
                    throw new ApplicationException(string.Format(@"Invalid priority {0} must be 0<=p<=7.", value));
                else if (priority != value)
                {
                    priority = value;
                    isChanged = true;
                }
            }
        }

        private bool isChanged = false;
        public bool IsChanged
        {
            get { return isChanged; }
            set { isChanged = value; }
        }

        /// <summary>
        /// Creates an instance of the OktoQueue class.
        /// </summary>
        /// <param name="flowId">FlowId uniqueue within this tenant.</param>
        /// <param name="conn">Connection for comms with the remote network agent.</param>
        internal OktoQueue(uint flowId, Connection conn)
        {
            this.flowId = flowId;
            this.conn = conn;
            vecLen = 1;
            TokSecVec[0] = ulong.MaxValue;
            bucketCapacity[0] = ulong.MaxValue;
            listRap = new List<RAP>();
            conn.ListQueues.Add(this);
            costFuncVecRead = new CostFuncVec();
            costFuncVecWrite = new CostFuncVec();
        }

        public void Update()
        {
            isChanged = false;
        }

        public bool IsFlagOn(OktoFlowFlags flag)
        {
            return ((flags & (byte)flag) != 0);
        }

        public void SetFlagOn(OktoFlowFlags flag)
        {
            flags |= (byte)flag;
        }

        public void SetFlagOff(OktoFlowFlags flag)
        {
            if (IsFlagOn(flag))
            {
                byte bFlag = (byte)flag;
                flags &= ((byte)~bFlag);
                isChanged = true;
                Console.WriteLine("Flow{0} FlagOff({1})", FlowId, flag);
            }
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int dbgOrgOffset = offset;
            offset += Utils.Int32ToNetBytes((int)FlowId, buffer, offset);
            for (uint VecIdx = 0; VecIdx < Parameters.OKTO_MAX_VEC_LEN; VecIdx++)
            {
                offset += Utils.Int64ToNetBytes((long)tokSecVec[VecIdx], buffer, offset);
                offset += Utils.Int64ToNetBytes((long)bucketCapacity[VecIdx], buffer, offset);
            }
            Debug.Assert(offset == dbgOrgOffset + SIZEOF_QOS_ARG);
            return SIZEOF_QOS_ARG;
        }

        public int SerializeCreate(byte[] buffer, int offset)
        {
            int dbgOrgOffset = offset;
            offset += Serialize(buffer, offset);
            offset += costFuncVecRead.Serialize(buffer, offset);
            offset += costFuncVecWrite.Serialize(buffer, offset);
            buffer[offset++] = vecLen;
            buffer[offset++] = priority;
            buffer[offset++] = Flags;
            Debug.Assert(offset == dbgOrgOffset + SIZEOF_QOS_ARG_CREATE);
            return SIZEOF_QOS_ARG_CREATE;
        }
    }
}
