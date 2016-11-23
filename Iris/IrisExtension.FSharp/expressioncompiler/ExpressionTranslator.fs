// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace IrisExtension.ExpressionCompiler

open System
open IrisCompiler
open IrisCompiler.FrontEnd
open Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation

/// <summary>
/// A subclass of the Translator class that is specific to compiling expressions in the debugger.
/// </summary>
type ExpressionTranslator(context : DebugCompilerContext) =
    inherit Translator(context)

    let lexer = context.Lexer

    override this.TranslateInput() =
        // Parse the expression first to determine the result type
        this.MethodGenerator.SetOutputEnabled(false)
        let resultType = this.ParseExpressionAndReadFormatSpecifiers()
        this.MethodGenerator.SetOutputEnabled(true)

        if context.ErrorCount = 0 then
            // No errors: Now that we know the result type, parse again and generate code this time

            context.Emitter.BeginProgram(context.ClassName, context.Importer.ImportedAssemblies)
            lexer.Reset()

            this.MethodGenerator.BeginMethod(context.MethodName, resultType, context.ParameterVariables, context.LocalVariables, false, String.Empty)
            this.ParseExpression() |> ignore
            this.MethodGenerator.EndMethod()

            let mutable readOnly = resultType.IsArray || resultType = IrisType.Void

            if context.ErrorCount = 0 then
                context.Emitter.EndProgram()

                if not readOnly then
                    // As a final step, see if this expression is something that can be assigned
                    // to.  We only support very simple L-Values for assignments so if the syntax
                    // doesn't match exactly, make the result read only.
                    lexer.Reset()
                    readOnly <- not (this.TryParseAsLValue())

            if resultType = IrisType.Boolean then
                // Setting the "BoolResult" flag allows the expression to be used for a
                // "when true" breakpoint condition.
                context.ResultFlags <- context.ResultFlags ||| DkmClrCompilationResultFlags.BoolResult

            if this.ParsedCallSyntax then
                // If we parsed call syntax, this expression has the potential to have side
                // effects.  Setting the PotentialSideEffect flag prevents the debugger from
                // implicitly evaluating the expression without user interaction.
                // Instead of implicity evaluating the expression, the debugger will show the
                // message "This expression has side effects and will not be evaluated"
                context.ResultFlags <- context.ResultFlags ||| DkmClrCompilationResultFlags.PotentialSideEffect;

                readOnly <- true // Can't modify return value of call

            if readOnly then
                context.ResultFlags <- context.ResultFlags ||| DkmClrCompilationResultFlags.ReadOnlyResult

    member private this.ParseExpressionAndReadFormatSpecifiers() : IrisType =
        // Call the base class to parse the expression.
        // Then do our own parsing to handle format specifiers.
        let resultType = this.ParseExpression()

        // If no compile errors, look for format specifiers
        if context.CompileErrors.Count > 0 then
            resultType
        else if lexer.CurrentToken = Token.Eof then
            resultType
        else
            while lexer.CurrentToken = Token.ChrComma do
                lexer.MoveNext()
                if lexer.CurrentToken = Token.Identifier then
                    context.FormatSpecifiers.Add(lexer.GetLexeme())
                    lexer.MoveNext()
                else
                    this.AddErrorAtTokenStart("Invalid format specifier.")

            if lexer.CurrentToken <> Token.Eof then
                this.AddErrorAtTokenStart("Unexpected text after expression.")

            resultType

    member private this.TryParseAsLValue() =
        if not (this.Accept(Token.Identifier)) then
            false
        else
            let anyBracketsMatched =
                if this.Accept(Token.ChrOpenBracket) then
                    // We allow assignment to array elements as long as the subscript is just a number.
                    if not (this.Accept(Token.Number)) then
                        false
                    else if not (this.Accept(Token.ChrCloseBracket)) then
                        false
                    else
                        true
                else
                    true

            anyBracketsMatched && (this.Accept(Token.Eof))
