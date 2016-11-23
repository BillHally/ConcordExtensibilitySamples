// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace IrisExtension.ExpressionCompiler

open IrisCompiler
open IrisCompiler.BackEnd
open IrisCompiler.FrontEnd
open Microsoft.VisualStudio.Debugger.Evaluation
open Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation
open System.Collections.Generic

/// <summary>
/// A subclass of the Translator class that is specific to generating IL to get local
/// variable values in the debugger.
/// </summary>
type LocalVariablesTranslator(context : DebugCompilerContext) as this =
    inherit Translator(context)

    let _functions = new Dictionary<IrisType, Function>()
    let _context   = context

    override __.TranslateInput() =
        _context.Emitter.BeginProgram(_context.ClassName, _context.Importer.ImportedAssemblies)

        for global' in _context.SymbolTable.Global do
            this.MaybeGenerateEntryForSymbol(global')

        for local in _context.SymbolTable.Local do
            this.MaybeGenerateEntryForSymbol(local)

        _context.Emitter.EndProgram()

    member private this.MaybeGenerateEntryForSymbol(symbol : Symbol) =
        if _context.ArgumentsOnly && symbol.StorageClass <> StorageClass.Argument then
            // We are only showing arguments
            ()
        else
            let symbolType = symbol.Type
            if symbolType.IsMethod || symbolType = IrisType.Invalid || symbolType = IrisType.Void then
                // This symbol doesn't belong in the Locals window.
                // Don't generate an entry for it.
                ()
            else if symbol.Name.StartsWith("$.") then
                // Don't show compiler internal symbols
                ()
            else
                let methodName = _context.NextMethodName()
                let derefType = this.DerefType(symbolType)

                // Emit code for the method to get the symbol value.
                this.MethodGenerator.BeginMethod(methodName, derefType, _context.ParameterVariables, _context.LocalVariables, entryPoint = false, methodFileName = null)
                this.EmitLoadSymbol(symbol, Translator.SymbolLoadMode.Dereference)
                this.MethodGenerator.EndMethod()

                // Generate the local entry to pass back to the debug engine
                let resultFlags =
                    if derefType = IrisType.Boolean then
                        // The debugger uses "BoolResult" for breakpoint conditions so setting the flag
                        // here has no effect currently, but we set it for the sake of consistency.
                        DkmClrCompilationResultFlags.BoolResult
                    else if derefType.IsArray then
                        // Iris doesn't support modification of an array itself
                        DkmClrCompilationResultFlags.ReadOnlyResult
                    else DkmClrCompilationResultFlags.None

                let fullName = symbol.Name

                DkmClrLocalVariableInfo.Create(
                    fullName,
                    fullName,
                    methodName,
                    resultFlags,
                    DkmEvaluationResultCategory.Data,
                    null)
                |> _context.GeneratedLocals.Add

