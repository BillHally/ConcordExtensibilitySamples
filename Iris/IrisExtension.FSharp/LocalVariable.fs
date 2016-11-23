// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace IrisExtension

open IrisCompiler

/// <summary>
/// LocalVariable is a pairing of an Iris Variable and the slot number the value is stored in.
/// </summary>
type LocalVariable(name : string, type' : IrisType, slot : int) =

    let variable = new Variable(type', name)

    member __.Variable = variable
    member __.Slot = slot
    member __.Name = variable.Name
    member __.Type = variable.Type
