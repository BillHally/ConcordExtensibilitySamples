// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace IrisExtension.ExpressionCompiler

open System
open System.Collections.Generic
open System.IO
open System.Linq

open IrisCompiler
open IrisCompiler.BackEnd
open IrisCompiler.FrontEnd
open IrisCompiler.Import
open Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation

open IrisExtension

module private DebugCompilerContextFlags =
    [<Literal>]
    let Flags = CompilationFlags.NoDebug ||| CompilationFlags.WriteDll

/// <summary>
/// A subclass of the CompilerContext class to deal with all of the context for the various
/// types of compilation we need to do in the debugger.
/// </summary>
type DebugCompilerContext
    (
        ownedSession     : InspectionSession option,
        scope            : InspectionScope,
        input            : MemoryStream,
        reader           : StreamReader,
        translatorType   : System.Type,
        methodName       : string,
        generatedLocals  : List<DkmClrLocalVariableInfo>,
        assignmentLValue : string,
        argumentsOnly    : bool
    )
    =
    inherit CompilerContext("fake.iris", reader, scope.Importer, new PeEmitter(DebugCompilerContextFlags.Flags), DebugCompilerContextFlags.Flags)

    static let incr (x : uint32 ref) =
        x := !x + 1u
        !x

    static let s_nextClass = ref 0u

    let isValidMethod (method' : Method) =
        match method' with
        | :? Function as func when func <> null && func.ReturnType = IrisType.Invalid ->
            false
        | method' ->
            method'.GetParameters()
            |> Seq.tryFind(fun x -> x.Type = IrisType.Invalid)
            |> Option.isNone

    let mutable disposed = false

    let _ownedSession   = ownedSession
    let _input          = input
    let _reader         = reader
    let _translatorType = translatorType

    let formatSpecifiers = new List<string>()

    let mutable _irisMethod : Method option = None
    let _nextMethod = ref 0u
   
    member __.GeneratedLocals  = generatedLocals
    member __.FormatSpecifiers = formatSpecifiers
    member __.Scope            = scope
    member __.AssignmentLValue = assignmentLValue
    member __.ClassName        = String.Format("$.C{0}", incr s_nextClass)
    member __.MethodName       = methodName
    member __.ArgumentsOnly    = argumentsOnly

    member val ResultFlags = DkmClrCompilationResultFlags.None with get, set

    member __.ParameterVariables =
        match _irisMethod with
        | None   -> [||]
        | Some x -> x.GetParameters()

    member __.LocalVariables =
        scope.GetLocals().Select(fun v -> v.Variable).ToArray()

    member this.GetPeBytes() : byte[] =
        if this.ErrorCount = 0 then
            this.Emitter.Flush()
            (this.Emitter :?> PeEmitter).GetPeBytes()
        else
            null // TODO: do we want to do this?

    member this.InitializeSymbols() =
        match scope.TryImportCurrentMethod() with
        | null -> () // Nothing to evaluate if we can't get the current method
        | currentMethod ->
            // Add compiler intrinsics
            this.AddIntrinsics()

            // Add debugger intrinsics
            // (Not implemented yet)

            // Add globals
            let type' = currentMethod.DeclaringType

            for importedfield in type'.GetFields() do
                let irisType = importedfield.FieldType
                if (irisType <> IrisType.Invalid) then
                    this.SymbolTable.Add(importedfield.Name, irisType, StorageClass.Global, importedfield) |> ignore

            // Add methods
            for importedMethod in type'.GetMethods() do
                let method' = importedMethod.ConvertToIrisMethod()
                if isValidMethod method' then
                    this.SymbolTable.Add(importedMethod.Name, method', StorageClass.Global, importedMethod) |> ignore

            // Create symbol for query method and transition the SymbolTable to method scope
            let irisMethod = currentMethod.ConvertToIrisMethod()
            this.SymbolTable.OpenMethod("$.query", irisMethod) |> ignore
            _irisMethod <- Some irisMethod

            // Add symbols for parameters
            for param in irisMethod.GetParameters() do
                this.SymbolTable.Add(param.Name, param.Type, StorageClass.Argument) |> ignore

            // Add symbols for local variables
            for local in scope.GetLocals() do
                this.SymbolTable.Add(local.Name, local.Type, StorageClass.Local, local.Slot) |> ignore

    member this.GenerateQuery() = this.Translator.TranslateInput()

    member __.NextMethodName() = String.Format("$.M{0}", incr _nextMethod)

    override __.Dispose(disposing : bool) =
        if disposing && (not disposed) then
            disposed <- true
            if _reader <> null then _reader.Dispose()
            if _input  <> null then _input.Dispose()
            match _ownedSession with
            | Some x -> x.Dispose()
            | None   -> ()

        base.Dispose(disposing)

    override __.ReferenceMscorlib() : ImportedModule = scope.ImportMscorlib()
    override __.ReferenceExternal(moduleName) : ImportedModule = scope.ImportModule(moduleName)
    override this.CreateTranslator() : Translator = Activator.CreateInstance(translatorType, this) :?> Translator
