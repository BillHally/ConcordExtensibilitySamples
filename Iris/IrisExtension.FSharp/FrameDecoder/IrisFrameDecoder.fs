// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace IrisExtension.FrameDecoder

open IrisCompiler
open IrisCompiler.Import
open Microsoft.VisualStudio.Debugger
open Microsoft.VisualStudio.Debugger.CallStack
open Microsoft.VisualStudio.Debugger.Clr
open Microsoft.VisualStudio.Debugger.ComponentInterfaces
open Microsoft.VisualStudio.Debugger.Evaluation
open System
open System.Text

open IrisExtension

/// <summary>
/// This class is the entry point into the Frame Decoder.  The frame decoder is used to provide
/// the text shown in the Call Stack window or other places in the debugger UI where stack
/// frames are used.  See the method comments below for more details about each method.
/// </summary>
type IrisFrameDecoder() =
    interface IDkmLanguageFrameDecoder with
        /// <summary>
        /// This method is called by the debug engine to get the text representation of a stack
        /// frame.
        /// </summary>
        /// <param name="inspectionContext">Context of the evaluation.  This contains options/flags
        /// to be used during compilation. It also contains the InspectionSession.  The inspection
        /// session is the object that provides lifetime management for our objects.  When the user
        /// steps or continues the process, the debug engine will dispose of the inspection session</param>
        /// <param name="workList">The current work list.  This is used to batch asynchronous
        /// work items.  If any asynchronous calls are needed later, this is the work list to pass
        /// to the asynchronous call.  It's not needed in our case.</param>
        /// <param name="frame">The frame to get the text representation for</param>
        /// <param name="argumentFlags">Option flags to change the way we format frames</param>
        /// <param name="completionRoutine">Completion routine to call when work is completed</param>
        member __.GetFrameName
            (
                inspectionContext : DkmInspectionContext,
                workList : DkmWorkList,
                frame : DkmStackWalkFrame,
                argumentFlags : DkmVariableInfoFlags,
                completionRoutine : DkmCompletionRoutine<DkmGetFrameNameAsyncResult>
            ) =
            let name =
                match IrisFrameDecoder.TryGetFrameNameHelper(inspectionContext, frame, argumentFlags) with
                | null -> "<Unknown Method>"
                | x    -> x

            completionRoutine.Invoke(new DkmGetFrameNameAsyncResult(name))

        /// <summary>
        /// This method is called by the debug engine to get the text representation of the return
        /// value of a stack frame.
        /// </summary>
        /// <param name="inspectionContext">Context of the evaluation.  This contains options/flags
        /// to be used during compilation. It also contains the InspectionSession.  The inspection
        /// session is the object that provides lifetime management for our objects.  When the user
        /// steps or continues the process, the debug engine will dispose of the inspection session</param>
        /// <param name="workList">The current work list.  This is used to batch asynchronous
        /// work items.  If any asynchronous calls are needed later, this is the work list to pass
        /// to the asynchronous call.  It's not needed in our case.</param>
        /// <param name="frame">The frame to get the text representation of the return value for</param>
        /// <param name="completionRoutine">Completion routine to call when work is completed</param>
        member this.GetFrameReturnType
            (
                inspectionContext : DkmInspectionContext,
                workList : DkmWorkList,
                frame : DkmStackWalkFrame,
                completionRoutine : DkmCompletionRoutine<DkmGetFrameReturnTypeAsyncResult>
            ) =
            let name =
                match IrisFrameDecoder.TryGetFrameReturnTypeHelper(inspectionContext, frame) with
                | null -> "<Unknown>"
                | x    -> x

            completionRoutine.Invoke(new DkmGetFrameReturnTypeAsyncResult(name))

    static member private TryGetFrameReturnTypeHelper(inspectionContext : DkmInspectionContext, frame : DkmStackWalkFrame) =
        match IrisFrameDecoder.TryGetCurrentMethod(inspectionContext, frame) with
        | null          -> null
        | currentMethod -> currentMethod.ReturnType.ToString()

    static member private TryGetFrameNameHelper(inspectionContext : DkmInspectionContext, frame : DkmStackWalkFrame, argumentFlags : DkmVariableInfoFlags) =
        match IrisFrameDecoder.TryGetCurrentMethod(inspectionContext, frame) with
        | null -> null
        | currentMethod ->
            let name = currentMethod.Name
            if String.Equals(name, "$.main", StringComparison.Ordinal) then
                "<Main Block>"
            else if argumentFlags = DkmVariableInfoFlags.None then
                name
            else
                let args = currentMethod.GetParameters()
                if args.Length = 0 then
                    name
                else
                    let nameBuilder = new StringBuilder()
                    nameBuilder.Append(name) |> ignore
                    nameBuilder.Append('(')  |> ignore

                    let mutable first = true
                    let showTypes = argumentFlags.HasFlag(DkmVariableInfoFlags.Types)
                    let showNames = argumentFlags.HasFlag(DkmVariableInfoFlags.Names)

                    for arg in args do
                        if first then first <- false
                        else
                            nameBuilder.Append("; ") |> ignore

                        let argType =
                            if arg.Type.IsByRef then
                                nameBuilder.Append("var ") |> ignore
                                arg.Type.GetElementType()
                            else
                                arg.Type

                        if showNames then
                            nameBuilder.Append(arg.Name) |> ignore

                        if showNames && showTypes then
                            nameBuilder.Append(" : ") |> ignore

                        if showTypes then
                            nameBuilder.Append(argType) |> ignore

                    nameBuilder.Append(')') |> ignore
                    nameBuilder.ToString()

    static member private TryGetCurrentMethod(inspectionContext : DkmInspectionContext, frame : DkmStackWalkFrame) : ImportedMethod =
        let session = InspectionSession.GetInstance(inspectionContext.InspectionSession)
        let scope = session.GetScope(frame.InstructionAddress :?> DkmClrInstructionAddress)

        scope.TryImportCurrentMethod()
