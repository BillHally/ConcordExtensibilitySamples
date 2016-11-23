// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace IrisExtension.ExpressionCompiler

open System
open System.Collections.Generic
open System.IO
open System.Text

open Microsoft.VisualStudio.Debugger.Clr
open Microsoft.VisualStudio.Debugger.Evaluation
open Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation

open IrisExtension

/// <summary>
/// Factory for creating instances of DebugCompilerContext.  We use a factory here because
/// creation of the context is somewhat non-trivial.
/// </summary>
module ContextFactory =
    let private createInputStream (expression : string) =
        let buffer = Encoding.Default.GetBytes(expression)
        let input = new MemoryStream(buffer)
        let reader = new StreamReader(input)
        input, reader

    let createExpressionContext (inspectionContext : DkmInspectionContext, address : DkmClrInstructionAddress, expression : string) : DebugCompilerContext =
        let ownedSession, scope =
            if inspectionContext <> null then
                let session = InspectionSession.GetInstance(inspectionContext.InspectionSession)
                None, session.GetScope(address)
            else
                // There is no inspection context when compiling breakpoint conditions.  Create a
                // new temporary session.  The context will need to dispose of this new session
                // when it is disposed.
                let ownedSession = new InspectionSession()
                Some ownedSession, ownedSession.GetScope(address)

        let input, reader = createInputStream expression

        let context =
            new DebugCompilerContext
                (
                    ownedSession, 
                    scope,
                    input,
                    reader,
                    typeof<ExpressionTranslator>,
                    "$.M1",
                    null, // Generated locals is not applicable for compiling expressions
                    null, // Assignment L-Value only applies to assigments
                    false // "ArgumentsOnly" only applies to local variable query
                )

        context.InitializeSymbols()

        context

    let createAssignmentContext (lValue : DkmEvaluationResult, address : DkmClrInstructionAddress, expression : string) : DebugCompilerContext =
        let input, reader = createInputStream expression

        let session = InspectionSession.GetInstance(lValue.InspectionSession)
        let scope = session.GetScope(address)

        let context =
            new DebugCompilerContext
                (
                    None, // None because the context doesn't own the lifetime of the session
                    scope,
                    input,
                    reader,
                    typeof<AssignmentTranslator>,
                    "$.M1",
                    null, // Generated locals is not applicable for assigments
                    lValue.FullName,
                    false // "ArgumentsOnly" only applies to local variable query
                )

        context.InitializeSymbols()

        context

    let createLocalsContext (inspectionContext : DkmInspectionContext, address : DkmClrInstructionAddress, argumentsOnly : bool) : DebugCompilerContext =
        let input, reader = createInputStream String.Empty

        let session = InspectionSession.GetInstance(inspectionContext.InspectionSession)
        let scope = session.GetScope(address)

        let context =
            new DebugCompilerContext
                (
                    None, // None because the context doesn't own the lifetime of the session
                    scope,
                    input,
                    reader,
                    typeof<LocalVariablesTranslator>,
                    null, // Method name is not applicable because we create multiple methods for Locals
                    new List<DkmClrLocalVariableInfo>(),
                    null, // Assignment L-Value only applies to assigments
                    argumentsOnly
                )

        context.InitializeSymbols()

        context
