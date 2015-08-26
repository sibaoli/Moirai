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
    /// <summary>
    /// Allows us to specify a piecewise cost function for a given queue.
    /// The cost function will be stored and applied by the associated minifilter
    /// to calculate the number of tokens required for (consumed by) an IRP of a
    /// given type (READ or WRITE) and a given bytecount.
    /// </summary>
    public class CostFunction
    {
        const int INSTU_COST_FUNC_LENGTH = 4;
        const int INSTU_COST_FUNC_MAX_INDEX = INSTU_COST_FUNC_LENGTH - 1;
        public const int SIZEOF_COST_FUNC =  // wire size
                                          4    // HighestIndex
                                          + 16   // xArray = uint * 4
                                          + 16;  // fxArray = uint * 4
        private readonly uint HighestIndex = 0;
        private readonly uint[] xArray = new uint[INSTU_COST_FUNC_LENGTH];
        private readonly uint[] fxArray = new uint[INSTU_COST_FUNC_LENGTH];

        public CostFunction(uint[] xArray, uint[] fxArray)
        {
            if (xArray == null || fxArray == null)
                throw new NullReferenceException("xArray and fxArray args cannot be null.");
            if (xArray.Length != fxArray.Length)
                throw new ApplicationException("xArray and fxArray not equal length.");
            if (xArray.Length < 2)
                throw new ApplicationException("xArray and fxArray too short (min 2 elements).");
            if (xArray.Length > INSTU_COST_FUNC_LENGTH)
                throw new ApplicationException("xArray and fxArray too long.");
            if (xArray[0] != 0)
                throw new ApplicationException("xArray[0] must be equal to zero.");
            HighestIndex = (uint)(xArray.Length - 1);
            for (int i = 0; i <= HighestIndex; i++)
            {
                this.xArray[i] = xArray[i];
                this.fxArray[i] = fxArray[i];
            }
        }

        public static CostFunction GetNullCostFunction()
        {
            uint[] nullSegments = new uint[INSTU_COST_FUNC_LENGTH];
            CostFunction CostFunc = new CostFunction(nullSegments, nullSegments);
            return CostFunc;
        }

        public static CostFunction GetIdentityCostFunction()
        {
            CostFunction CostFunc = new CostFunction(new uint[] { 0, uint.MaxValue }, new uint[] { 0, uint.MaxValue });
            return CostFunc;
        }

        public int Serialize(byte[] buffer, int offset)
        {
            int index;
            int dbgOrgOffset = offset;
            offset += Utils.Int32ToNetBytes((int)HighestIndex, buffer, offset);
#if gregos
            for (index = 0; index < INSTU_COST_FUNC_LENGTH; index++)
            {
                int arg = (index <= HighestIndex ? (int)xArray[index] : 0 );
                offset += Utils.Int32ToNetBytes(arg, buffer, offset);
            }
            for (index = 0; index < INSTU_COST_FUNC_LENGTH; index++)
            {
                int arg = (index <= HighestIndex ? (int)fxArray[index] : 0);
                offset += Utils.Int32ToNetBytes(arg, buffer, offset);
            }
#else
            for (index = 0; index < INSTU_COST_FUNC_LENGTH; index++)
            {
                int x = (index <= HighestIndex ? (int)xArray[index] : 0);
                offset += Utils.Int32ToNetBytes(x, buffer, offset);
                int fx = (index <= HighestIndex ? (int)fxArray[index] : 0);
                offset += Utils.Int32ToNetBytes(fx, buffer, offset);
            }
#endif
            Debug.Assert(offset == dbgOrgOffset + SIZEOF_COST_FUNC);
            return SIZEOF_COST_FUNC;
        }
    }

    public class CostFuncVec
    {
        const int OKTO_MAX_VEC_LEN = Parameters.OKTO_MAX_VEC_LEN;
        public const int SIZEOF_COST_FUNC_VEC =
            CostFunction.SIZEOF_COST_FUNC * OKTO_MAX_VEC_LEN;

        private readonly CostFunction[] vector;

        public CostFunction[] Vector { get { return vector; } }

        public CostFuncVec()
        {
            vector = new CostFunction[OKTO_MAX_VEC_LEN];
            for (int VecIdx = 0; VecIdx < OKTO_MAX_VEC_LEN; VecIdx++)
                vector[VecIdx] = CostFunction.GetIdentityCostFunction();
        }

        public CostFuncVec(CostFunction[] arrayCostFunc)
        {
            if (arrayCostFunc == null)
                throw new ArgumentNullException("arrayCostFunc");
            else if (arrayCostFunc.Length < 1)
                throw new ArgumentOutOfRangeException(string.Format("vecLen {0} < 1", arrayCostFunc.Length));
            else if (arrayCostFunc.Length > OKTO_MAX_VEC_LEN)
                throw new ArgumentOutOfRangeException(string.Format("vecLen {0} > MAX_VEC_LEN", arrayCostFunc.Length));
            else if (arrayCostFunc.Length == OKTO_MAX_VEC_LEN)
                vector = arrayCostFunc;
            else
            {
                vector = new CostFunction[OKTO_MAX_VEC_LEN];
                for (int VecIdx = 0; VecIdx < OKTO_MAX_VEC_LEN; VecIdx++)
                {
                    if (VecIdx < arrayCostFunc.Length)
                        vector[VecIdx] = arrayCostFunc[VecIdx];
                    else
                        vector[VecIdx] = CostFunction.GetIdentityCostFunction();
                }
            }
        }

        public CostFunction GetCostFunc(int index)
        {
            if (index < 0 || index >= OKTO_MAX_VEC_LEN)
                throw new ArgumentOutOfRangeException("index");
            return vector[index];
        }

        public void SetCostFunc(CostFunction costFunc, uint index)
        {
            if (index >= OKTO_MAX_VEC_LEN)
                throw new ArgumentOutOfRangeException("index");
            vector[index] = costFunc;
        }


        public int Serialize(byte[] buffer, int offset)
        {
            for (uint VecIdx = 0; VecIdx < OKTO_MAX_VEC_LEN; VecIdx++)
                offset += vector[VecIdx].Serialize(buffer, offset);
            return SIZEOF_COST_FUNC_VEC;
        }
    }

}
