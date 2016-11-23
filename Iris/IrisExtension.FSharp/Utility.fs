// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace IrisExtension

open IrisCompiler
type Type = Microsoft.VisualStudio.Debugger.Metadata.Type

module Utility =
    /// <summary>
    /// Convert a type from the debugger's type system into Iris's type system
    /// </summary>
    /// <param name="lmrType">LMR Type</param>
    /// <returns>Iris type</returns>
    let rec getIrisTypeForLmrType (lmrType : Type) : IrisType =
        if lmrType.IsPrimitive && lmrType.FullName = "System.Int32" then
            IrisType.Integer
        else if lmrType.IsPrimitive && lmrType.FullName = "System.Boolean" then
            IrisType.Boolean
        else if lmrType.IsArray then
            if lmrType.GetArrayRank() <> 1 then
                IrisType.Invalid
            else
                let elementType = getIrisTypeForLmrType (lmrType.GetElementType())
                if elementType = IrisType.Invalid then
                    IrisType.Invalid
                else
                    elementType.MakeArrayType()
        else if lmrType.IsByRef then
            let elementType = getIrisTypeForLmrType (lmrType.GetElementType())
            if elementType = IrisType.Invalid then
                IrisType.Invalid
            else
                elementType.MakeByRefType()
        else if lmrType.FullName.Equals("System.String") then
            IrisType.String
        else
            IrisType.Invalid
