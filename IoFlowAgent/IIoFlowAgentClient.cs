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
using System.Net.Sockets;

namespace IoFlowAgentNamespace
{
    /// <summary>
    /// Things that want to use the IoFlowAgent class must implement this interface.
    /// </summary>
    public interface IIoFlowAgentClient
    {
        /// <summary>
        /// Message handler for MessageRegister.
        /// </summary>
        void CallbackMessageRegister(Connection conn, uint tenantId, UInt64 alertVec);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="listFlowParams"></param>
        /// <param name="listRapParams"></param>
        /// <returns></returns>
        OktoResultCodes CallbackIoFlowCreate(
            Dictionary<uint, IoFlowMessageParams> dictFlowParams,
            Dictionary<uint, MsgRapArg> dictRapParams);

        /// <summary>
        /// Message handler for MessageStatsZero.
        /// </summary>
        OktoResultCodes CallbackMessageStatsZero();

        /// <summary>
        /// Message handler for MessageIoFlowUpdate.
        /// </summary>
        OktoResultCodes CallbackMessageIoFlowUpdate(List<IoFlowMessageParams> listParams);

        /// <summary>
        /// Message handler for MessageIoFlowStatsQuery.
        /// </summary>
        List<IoFlowMessageParams> CallbackMessageIoFlowStatsQuery();

        /// <summary>
        /// Message handler for MessageTenantDelete.
        /// </summary>
        OktoResultCodes CallbackMessageTenantDelete();

        /// <summary>
        /// Callback when socket reports close.
        /// </summary>
        /// <param name="conn"></param>
        void CallbackReceiveClose(Connection conn);

        /// <summary>
        /// Callback when socket reports and error.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="errNo"></param>
        void CallbackReceiveError(Connection conn, int errNo);

        /// <summary>
        /// Callback when socket exception has been caught.
        /// </summary>
        /// <param name="sockEx"></param>
        void CallbackCatchSocketException(SocketException sockEx);

    }
}
