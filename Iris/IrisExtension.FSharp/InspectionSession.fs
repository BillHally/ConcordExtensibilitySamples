// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace IrisExtension
    
open IrisCompiler.Import
open Microsoft.VisualStudio.Debugger
open Microsoft.VisualStudio.Debugger.Clr
open Microsoft.VisualStudio.Debugger.Evaluation
open System
open System.Collections.Generic

/// <summary>
/// This class is our representation of the inspection session.  We add this as a data item to
/// the debug engine's DkmInspectionContext.  When the user steps or continues the process, the
/// debug engine disposes of the DkmInspectionContext and our inspection session along with it.
/// This allows us to tie the lifetime of our objects to lifetime of the inspection session.
/// </summary>
type InspectionSession() =
    inherit DkmDataItem()
    
    let scopes = new Dictionary<DkmClrInstructionAddress, InspectionScope>(AddressComparer.Instance)

    member val Importer = new Importer()

    override this.OnClose() = this.Dispose()

    member this.Dispose() = this.Importer.Dispose()

    static member GetInstance(dkmObject : DkmInspectionSession) =
        match dkmObject.GetDataItem<InspectionSession>() with
        | x when (x :> obj) = null -> // Cast to obj because InspectionSession is non-nullable (which doesn't guarantee it's already been set)
            let session = new InspectionSession();
            dkmObject.SetDataItem(DkmDataCreationDisposition.CreateNew, session)
            session
        | x -> x

    member this.GetScope(address : DkmClrInstructionAddress) : InspectionScope =
        // Cache the various scopes used during the inspection session.  Different scopes are
        // used when the user selects different frames and when the debug engine asks us to
        // format each stack frame.
        match scopes.TryGetValue(address) with
        | true, scope -> scope
        | false, _ ->
            let scope = new InspectionScope(address, this.Importer)
            scopes.Add(address, scope)
            scope

    interface IDisposable with
        member this.Dispose () = this.Dispose()

