// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace IrisExtension.ExpressionCompiler

open System
open IrisCompiler
open IrisCompiler.FrontEnd
open System.IO
open System.Text

/// <summary>
/// A subclass of the Translator class that is specific to assigning values in the debugger.
/// </summary>
type AssignmentTranslator(context : DebugCompilerContext) =
    inherit Translator(context)

    member private this.ParseLValue (lvalLexer : Lexer) : Symbol =
        let symbol =
            if lvalLexer.CurrentToken <> Token.Identifier then
                null
            else
                match context.SymbolTable.Lookup(lvalLexer.GetLexeme()) with
                | null   -> null
                | symbol ->
                    let lhs = symbol.Type
                    lvalLexer.MoveNext()
                    if lhs.IsArray then
                        // We should have an open bracket (We don't support changing the array value itself)
                        if lvalLexer.CurrentToken <> Token.ChrOpenBracket then
                            null
                        else
                            this.EmitLoadSymbol(symbol, IrisCompiler.FrontEnd.Translator.SymbolLoadMode.Raw)
                            lvalLexer.MoveNext()
                            if lvalLexer.CurrentToken <> Token.Number then
                                null
                            else
                                let index = lvalLexer.ParseInteger()
                                this.MethodGenerator.PushIntConst(index)
                                lvalLexer.MoveNext()
                                if lvalLexer.CurrentToken <> Token.ChrCloseBracket then
                                    null
                                else
                                    symbol
                    else if lhs.IsByRef then
                        this.EmitLoadSymbol(symbol, IrisCompiler.FrontEnd.Translator.SymbolLoadMode.Raw)
                        symbol
                    else
                        symbol

        if symbol |> isNull then
            this.AddLValueError()

        symbol

    override this.TranslateInput() : unit =
        context.Emitter.BeginProgram(context.ClassName, context.Importer.ImportedAssemblies)
        this.MethodGenerator.BeginMethod(context.MethodName, IrisType.Void, context.ParameterVariables, context.LocalVariables, false, String.Empty)
        // Parse the L-Value.
        // We use a seperate lexer for this because it's part of a different string.
        let buffer = Encoding.Default.GetBytes(context.AssignmentLValue)
        let input = new MemoryStream(buffer)
        let lvalLexer =
            use reader = new StreamReader(input)
            Lexer.Create(reader, context.CompileErrors)
        
        match this.ParseLValue lvalLexer with
        | null ->
            // Parsing the L-Value failed.  (Error message already generated)
            ()
        | lvalue ->
            let lhs = lvalue.Type
            // Parse the R-Value.
            let rhs = this.ParseExpression()
            if not (this.Accept(Token.Eof)) then
                this.AddErrorAtTokenStart("Unexpected text after expression.")

            // Now finish emitting the code to do the assignment.
            if rhs <> IrisType.Invalid then
                let lhs, hasElementType =
                    if lhs.IsByRef || lhs.IsArray then
                        lhs.GetElementType(), true
                    else
                        lhs, false

                if lhs <> rhs then
                    this.AddErrorAtLastParsedPosition("Cannot assign value.  Expression type doesn't match value type")
                else if hasElementType then
                    if lvalue.Type.IsArray then
                        this.MethodGenerator.StoreElement(lhs)
                    else
                        this.MethodGenerator.Store(lhs)
                else
                    this.EmitStoreSymbol(lvalue)

            if context.ErrorCount = 0 then
                this.MethodGenerator.EndMethod()
                context.Emitter.EndProgram()

    member private this.AddLValueError() : unit  =
        // We shouldn't get into this state because we get the L-value strings from the full
        // name that we generate.  The only way to get here are values that don't come from the
        // Iris compiler or bugs.  We'll show a generic message if we happen to get into this
        // state.
        this.AddError(FilePosition.Begin, "Cannot assign to this value")
    