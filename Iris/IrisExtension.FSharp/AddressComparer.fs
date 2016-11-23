// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace IrisExtension

open System
open Microsoft.VisualStudio.Debugger.Clr
open System.Collections.Generic

// <summary>
// Equality comparer to allow us to use DkmClrInstructionAddress as a dictionary key.
// </summary>
 type private AddressComparer() =

    static member val Instance = new AddressComparer()
 
    interface IEqualityComparer<DkmClrInstructionAddress> with
        member __.Equals(address1, address2) =
            if address1.ILOffset <> address2.ILOffset then
                false
            else if address1.MethodId <> address2.MethodId then
                false
            else
                // Also compare the module.  Reference equality works for comparing module instances.
                Object.ReferenceEquals(address1.ModuleInstance, address2.ModuleInstance)
 
        member __.GetHashCode(obj : DkmClrInstructionAddress) = (int obj.ILOffset) ^^^ (obj.MethodId.GetHashCode())
