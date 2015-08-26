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
using System.Security.Principal;

namespace IoFlowNamespace
{
    // The flag bits must match those of the OKTO_FLOW_FLAGS enum in ioflowuser.h
    public enum IoFlowFlags
    {
        Deleting       = 0x001, // reserved for use by minifilter.
        PreRead        = 0x002,
        PostRead       = 0x004,
        PreWrite       = 0x008,
        PostWrite      = 0x010,
        PreCreate      = 0x020,
        PostCreate     = 0x040,
        PreCleanup     = 0x080,
        PostCleanup    = 0x100,
        PreLockControl = 0x200,
        LateIoError    = 0x400,
    }

    public class IoFlow
    {
        private readonly uint flowId;
        private readonly uint flags;
        private object context;
        private readonly string accountName;
        private readonly string fileName;
        private readonly CallbackPreCreate callbackPreCreate = null;
        private readonly CallbackPostCreate callbackPostCreate = null;
        private readonly CallbackPreRead callbackPreRead = null;
        private readonly CallbackPostRead callbackPostRead = null;
        private readonly CallbackPreWrite callbackPreWrite = null;
        private readonly CallbackPostWrite callbackPostWrite = null;
        private readonly CallbackPreCleanup callbackPreCleanup = null;
        private readonly CallbackPostCleanup callbackPostCleanup = null;
        private readonly CallbackPreLockControl callbackPreLockControl = null;
        private readonly Queue<IRP> IrpList;
        private readonly SecurityIdentifier sid = null;

        public uint FlowId { get { return flowId; } }
        public uint Flags { get { return flags; } }
        public object Context { get { return context; } set { context = value; } }
        public string AccountName { get { return accountName; } }
        public string FileName { get { return fileName; } }
        public SecurityIdentifier Sid { get { return sid; } }
        public CallbackPreCreate PreCreate { get { return callbackPreCreate; } }
        public CallbackPostCreate PostCreate { get { return callbackPostCreate; } }
        public CallbackPreRead PreRead { get { return callbackPreRead; } }
        public CallbackPostRead PostRead { get { return callbackPostRead; } }
        public CallbackPreWrite PreWrite { get { return callbackPreWrite; } }
        public CallbackPostWrite PostWrite { get { return callbackPostWrite; } }
        public CallbackPreCleanup PreCleanup { get { return callbackPreCleanup; } }
        public CallbackPostCleanup PostCleanup { get { return callbackPostCleanup; } }
        public CallbackPreLockControl PreLockControl { get { return callbackPreLockControl; } }

        /// <summary>
        /// Creates an instance of a flow and registers it with this IoFlowRuntime instance.
        /// </summary>
        /// <param name="flowId">An id unique to this flow e.g. from GetNextFreeFlowId(). </param>
        /// <param name="context">Additional context you want to keep for this flow.</param>
        /// <param name="accountName">A Windows account name for use in I/O classification.</param>
        /// <param name="sid">A Windows SID for use in I/O classification.</param>
        /// <param name="fileName">A windows file name used in I/O classification.</param>
        /// <param name="callbackPreCreate">Optional PreOp callback on CREATE IRPs.</param>
        /// <param name="callbackPostCreate">Optional PostOp callback on CREATE IRPs.</param>
        /// <param name="callbackPreRead">Optional PreOp callback on READ IRPs.</param>
        /// <param name="callbackPostRead">Optional PostOp callback on READ IRPs.</param>
        /// <param name="callbackPreWrite">Optional PreOp callback on WRITE IRPs.</param>
        /// <param name="callbackPostWrite">Optional PostOp callback on READ IRPs.</param>
        /// <param name="callbackPreCleanup">Optional PreOp callback on CLEANUP IRPs.</param>
        /// <param name="callbackPostCleanup">Optional PostOp callback on CLEANUP IRPs.</param>
        /// <param name="callbackPreLockControl">Optional PreOp callback on LOCKCONTROL IRPs.</param>
        public IoFlow(
            uint flowId,
            object context,
            string accountName,
            SecurityIdentifier sid,
            string fileName,
            CallbackPreCreate callbackPreCreate,
            CallbackPostCreate callbackPostCreate,
            CallbackPreRead callbackPreRead,
            CallbackPostRead callbackPostRead,
            CallbackPreWrite callbackPreWrite,
            CallbackPostWrite callbackPostWrite,
            CallbackPreCleanup callbackPreCleanup,
            CallbackPostCleanup callbackPostCleanup,
            CallbackPreLockControl callbackPreLockControl)
        {
            this.flowId = flowId;
            this.context = context;
            this.accountName = accountName;
            this.fileName = fileName;
            this.sid = sid;
            flags = 0x00;
            flags |= (callbackPreCreate != null ? (uint)IoFlowFlags.PreCreate : flags);
            flags |= (callbackPostCreate != null ? (uint)IoFlowFlags.PostCreate : flags);
            flags |= (callbackPreRead != null ? (uint)IoFlowFlags.PreRead : flags);
            flags |= (callbackPostRead != null ? (uint)IoFlowFlags.PostRead : flags);
            flags |= (callbackPreWrite != null ? (uint)IoFlowFlags.PreWrite : flags);
            flags |= (callbackPostWrite != null ? (uint)IoFlowFlags.PostWrite : flags);
            flags |= (callbackPreCleanup != null ? (uint)IoFlowFlags.PreCleanup : flags);
            flags |= (callbackPostCleanup != null ? (uint)IoFlowFlags.PostCleanup : flags);
            flags |= (callbackPreLockControl != null ? (uint)IoFlowFlags.PreLockControl : flags);
            this.callbackPreCreate = (callbackPreCreate != null ? callbackPreCreate : InvalidPreCreate);
            this.callbackPostCreate = (callbackPostCreate != null ? callbackPostCreate : InvalidPostCreate);
            this.callbackPreRead = ( callbackPreRead != null ? callbackPreRead : InvalidPreRead );
            this.callbackPostRead = ( callbackPostRead != null ? callbackPostRead : InvalidPostRead );
            this.callbackPreWrite = ( callbackPreWrite != null ? callbackPreWrite : InvalidPreWrite );
            this.callbackPostWrite = ( callbackPostWrite != null ? callbackPostWrite : InvalidPostWrite );
            this.callbackPreCleanup = (callbackPreCleanup != null ? callbackPreCleanup : InvalidPreCleanup);
            this.callbackPostCleanup = (callbackPostCleanup != null ? callbackPostCleanup : InvalidPostCleanup);
            this.callbackPreLockControl = (callbackPreLockControl != null ? callbackPreLockControl : InvalidPreLockControl);
            IrpList = new Queue<IRP>();
        }

        public void Enqueue(IRP irp)
        {
            lock (IrpList)
            {
                IrpList.Enqueue(irp);
            }
        }

        public IRP Dequeue()
        {
            IRP irp = null;
            lock (IrpList)
            {
                irp = IrpList.Dequeue();
            }
            return irp;
        }

        private PreCreateReturnCode InvalidPreCreate(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PreCreate callback not registered.", FlowId));
        }

        private PostCreateReturnCode InvalidPostCreate(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PostCreate callback not registered.", FlowId));
        }

        private PreReadReturnCode InvalidPreRead(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PreRead callback not registered.",FlowId));
        }

        private PostReadReturnCode InvalidPostRead(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PostRead callback not registered.", FlowId));
        }

        private PreWriteReturnCode InvalidPreWrite(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PreWrite callback not registered.", FlowId));
        }

        private PostWriteReturnCode InvalidPostWrite(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PostWrite callback not registered.", FlowId));
        }

        private PreCleanupReturnCode InvalidPreCleanup(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PreCleanup callback not registered.", FlowId));
        }

        private PostCleanupReturnCode InvalidPostCleanup(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PostCleanup callback not registered.", FlowId));
        }

        private PreLockControlReturnCode InvalidPreLockControl(IRP irp)
        {
            throw new NotImplementedException(String.Format("FlowId {0} PreLockControl callback not registered.", FlowId));
        }

    }
}
