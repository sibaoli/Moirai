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

namespace IoFlowNamespace
{
    /// <summary>
    /// Callback signatures defined here (aka "delegates" in CSharp-speak).
    /// We tesrrict the return codes per callback to those we support in the driver.
    /// This allows type-checker to detect unsupported return codes at compile-time.
    /// </summary>

    //
    // The callback return values are taken from WinMain_BlueMP fltkernel.h
    //
    enum FLT_PREOP_CALLBACK_STATUS
    {
        FLT_PREOP_SUCCESS_WITH_CALLBACK,
        FLT_PREOP_SUCCESS_NO_CALLBACK,
        FLT_PREOP_PENDING,
        FLT_PREOP_DISALLOW_FASTIO,
        FLT_PREOP_COMPLETE,
        FLT_PREOP_SYNCHRONIZE
    }

    enum FLT_POSTOP_CALLBACK_STATUS 
    {
        FLT_POSTOP_FINISHED_PROCESSING,
        FLT_POSTOP_MORE_PROCESSING_REQUIRED
    }

    //
    // CREATE PreOp. Restricted subset of FLT_PREOP_CALLBACK_STATUS.
    //
    public enum PreCreateReturnCode
    {
        FLT_PREOP_SUCCESS_WITH_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_WITH_CALLBACK,
        FLT_PREOP_SUCCESS_NO_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_NO_CALLBACK,
    }
    public delegate PreCreateReturnCode CallbackPreCreate(IRP irp);

    //
    // CREATE PostOp.
    //
    public enum PostCreateReturnCode
    {
        FLT_POSTOP_FINISHED_PROCESSING = FLT_POSTOP_CALLBACK_STATUS.FLT_POSTOP_FINISHED_PROCESSING,
    }
    public delegate PostCreateReturnCode CallbackPostCreate(IRP irp);


    //
    // READ PreOp. Restricted subset of FLT_PREOP_CALLBACK_STATUS.
    //
    public enum PreReadReturnCode
    {
        FLT_PREOP_SUCCESS_WITH_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_WITH_CALLBACK,
        FLT_PREOP_SUCCESS_NO_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_NO_CALLBACK,
        FLT_PREOP_COMPLETE = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_COMPLETE,
    }
    public delegate PreReadReturnCode CallbackPreRead(IRP irp);

    //
    // READ PostOp.
    //
    public enum PostReadReturnCode
    {
        FLT_POSTOP_FINISHED_PROCESSING = FLT_POSTOP_CALLBACK_STATUS.FLT_POSTOP_FINISHED_PROCESSING,
    }
    public delegate PostReadReturnCode CallbackPostRead(IRP irp);


    //
    // WRITE PreOp. Restricted subset of FLT_PREOP_CALLBACK_STATUS.
    //
    public enum PreWriteReturnCode
    {
        FLT_PREOP_SUCCESS_WITH_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_WITH_CALLBACK,
        FLT_PREOP_SUCCESS_NO_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_NO_CALLBACK,
        FLT_PREOP_COMPLETE = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_COMPLETE,
    }
    public delegate PreWriteReturnCode CallbackPreWrite(IRP irp);

    //
    // WRITE PostOp.
    //
    public enum PostWriteReturnCode
    {
        FLT_POSTOP_FINISHED_PROCESSING = FLT_POSTOP_CALLBACK_STATUS.FLT_POSTOP_FINISHED_PROCESSING,
    }
    public delegate PostWriteReturnCode CallbackPostWrite(IRP irp);

    //
    // CLEANUP PreOp. Restricted subset of FLT_PREOP_CALLBACK_STATUS.
    //
    public enum PreCleanupReturnCode
    {
        FLT_PREOP_SUCCESS_WITH_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_WITH_CALLBACK,
        FLT_PREOP_SUCCESS_NO_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_NO_CALLBACK,
    }
    public delegate PreCleanupReturnCode CallbackPreCleanup(IRP irp);

    //
    // CLEANUP PostOp.
    //
    public enum PostCleanupReturnCode
    {
        FLT_POSTOP_FINISHED_PROCESSING = FLT_POSTOP_CALLBACK_STATUS.FLT_POSTOP_FINISHED_PROCESSING,
    }
    public delegate PostCleanupReturnCode CallbackPostCleanup(IRP irp);

    //
    // CLEANUP PreLockControl. Restricted subset of FLT_PREOP_CALLBACK_STATUS.
    //
    public enum PreLockControlReturnCode
    {
        FLT_PREOP_COMPLETE = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_COMPLETE,
        FLT_PREOP_SUCCESS_NO_CALLBACK = FLT_PREOP_CALLBACK_STATUS.FLT_PREOP_SUCCESS_NO_CALLBACK,
    }
    public delegate PreLockControlReturnCode CallbackPreLockControl(IRP irp);


}
