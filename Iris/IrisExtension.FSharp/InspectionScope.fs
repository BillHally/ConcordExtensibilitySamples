// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace IrisExtension

open IrisCompiler
open IrisCompiler.Import
open Microsoft.VisualStudio.Debugger
open Microsoft.VisualStudio.Debugger.Clr
open Microsoft.VisualStudio.Debugger.Symbols
open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Linq

/// <summary>
/// A scope to do evaluations in.
/// 
/// This class does translation from the debug engine's / CLR's understanding of the current
/// scope into scope information that's understood by the Iris compiler.
/// </summary>
type InspectionScope(address : DkmClrInstructionAddress, importer : Importer) =

    let _modules = new Dictionary<string, ImportedModule>(StringComparer.OrdinalIgnoreCase)

    let mutable _mscorlib      : ImportedModule  = null
    let mutable _currentMethod : ImportedMethod  = null
    let mutable _cachedLocals  : LocalVariable[] = null

    member __.InstructionAddress = address
    member __.SymModule          = address.ModuleInstance.Module
    member __.CurrentMethodToken = address.MethodId.Token
    member __.Importer           = importer

    member this.TryImportCurrentMethod() : ImportedMethod =
        if _currentMethod <> null then
            _currentMethod
        else
            let metadataBlock, blockSize =
                try
                    address.ModuleInstance.GetMetaDataBytesPtr()
                with
                | :? DkmException ->
                    // This can fail when dump debugging if the full heap is not available
                    IntPtr.Zero, 0u

            match metadataBlock, blockSize with
            | x, _ when x = IntPtr.Zero -> null
            | metadataBlock, blockSize ->
                let module' = importer.ImportModule(metadataBlock, blockSize)
                _currentMethod <- module'.GetMethod(address.MethodId.Token);
                _currentMethod

    member this.GetLocals() : LocalVariable[] =
        if _cachedLocals <> null then
            _cachedLocals
        else
            let method' = this.TryImportCurrentMethod()
            _cachedLocals <- this.GetLocalsImpl(method').ToArray()
            _cachedLocals

    member this.ImportMscorlib() : ImportedModule =
        if _mscorlib <> null then
            _mscorlib
        else
            let currentAppDomain = address.ModuleInstance.AppDomain
            if currentAppDomain.IsUnloaded then
                null
            else
                match
                    currentAppDomain.GetClrModuleInstances()
                    |> Seq.tryFind
                        (
                            fun moduleInstance ->
                                (not moduleInstance.IsUnloaded) &&
                                moduleInstance.ClrFlags.HasFlag(DkmClrModuleFlags.RuntimeModule)
                        )
                    with
                | Some moduleInstance ->
                    _mscorlib <- this.ImportModule(moduleInstance)
                    _mscorlib

                | None -> null

    member this.ImportModule(name : string) : ImportedModule =
        match _modules.TryGetValue(name) with
        | true, result -> result
        | false, _ ->
            let currentAppDomain = address.ModuleInstance.AppDomain
            if currentAppDomain.IsUnloaded then
                null
            else
                match
                    currentAppDomain.GetClrModuleInstances()
                    |> Seq.filter (fun x -> not x.IsUnloaded)
                    |> Seq.tryFind
                        (
                            fun moduleInstance ->
                                let path = moduleInstance.FullName
                                let fileName = Path.GetFileName(path)
                                String.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)
                        )
                    with
                | Some moduleInstance ->
                    let result = this.ImportModule(moduleInstance)
                    if result <> null then
                        _modules.Add(name, result)
                    result
                | None ->
                    null

    member this.GetLocalsImpl(method' : ImportedMethod) : LocalVariable seq =
        if method' <> null then
            // Get the local symbols from the PDB (symbol file).  If symbols aren't loaded, we
            // can't show any local variables
            let symbols = this.GetLocalSymbolsFromPdb().ToArray()
            // To determine the local types, we need to decode the local variable signature
            // token.  Get the token from the debugger, then use the Iris Compiler's importer
            // to get the variables types.  We can then construct the correlated list of local
            // types and names.
            let localVarSigToken = address.ModuleInstance.GetLocalSignatureToken(this.CurrentMethodToken)
            let localTypes = method'.Module.DecodeLocalVariableTypes(localVarSigToken)

            seq
                {
                    for localSymbol in symbols do
                        let slot = localSymbol.Slot
                        yield new LocalVariable(localSymbol.Name, localTypes.[slot], slot)
                }
        else
            Seq.empty

    member private this.GetLocalSymbolsFromPdb () : DkmClrLocalVariable seq =
        // We need symbols to get local variables
        if this.SymModule <> null then
            let scopes : DkmClrMethodScopeData[] = this.SymModule.GetMethodSymbolStoreData(address.MethodId)

            seq
                {
                    for scope in scopes do
                        if this.InScope(scope) then
                            yield! scope.LocalVariables
                }
        else
            Seq.empty

    member private this.ImportModule(debuggerModule : DkmClrModuleInstance) : ImportedModule =
        try
            let metadataBlock, blockSize = debuggerModule.GetMetaDataBytesPtr()
            importer.ImportModule(metadataBlock, blockSize)
        with
        | :? DkmException ->
            // This can fail when dump debugging if the full heap is not available
            null

    member private this.InScope(scope : DkmClrMethodScopeData) =
        let offset = address.ILOffset
        scope.ILRange.StartOffset <= offset && scope.ILRange.EndOffset >= offset
