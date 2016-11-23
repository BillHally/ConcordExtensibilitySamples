// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace IrisExtension.Formatter

open IrisCompiler
open Microsoft.VisualStudio.Debugger.Clr
open Microsoft.VisualStudio.Debugger.ComponentInterfaces
open Microsoft.VisualStudio.Debugger.Evaluation
open Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation
open System
open System.Collections.ObjectModel
open System.Linq
type Type = Microsoft.VisualStudio.Debugger.Metadata.Type

open IrisExtension

/// <summary>
/// This class is the main entry point into the Formatter.  The Formatter is used by the debug
/// engine to format the result of inspection queries into strings that can be shown to the
/// user.  See the method comments below for more details about each method.
/// </summary>
type IrisFormatter() =

    interface IDkmClrFormatter with
        /// <summary>
        /// This method is called by the debug engine to populate the text representing the type of
        /// a result.
        /// </summary>
        /// <param name="inspectionContext">Context of the evaluation.  This contains options/flags
        /// to be used during compilation. It also contains the InspectionSession.  The inspection
        /// session is the object that provides lifetime management for our objects.  When the user
        /// steps or continues the process, the debug engine will dispose of the inspection session</param>
        /// <param name="clrType">This is the raw type we want to format</param>
        /// <param name="customTypeInfo">If Expression Compiler passed any additional information
        /// about the type that doesn't exist in metadata, this parameter contais that information.</param>
        /// <param name="formatSpecifiers">A list of custom format specifiers that the debugger did
        /// not understand.  If you want special format specifiers for your language, handle them
        /// here.  The formatter should ignore any format specifiers it does not understand.</param>
        /// <returns>The text of the type name to display</returns>
        member __.GetTypeName
            (
                inspectionContext : DkmInspectionContext,
                clrType : DkmClrType,
                customTypeInfo : DkmClrCustomTypeInfo,
                formatSpecifiers : ReadOnlyCollection<string>
            ) : string =
            // Get the LMR type for the DkmClrType.  LMR Types (Microsoft.VisualStudio.Debugger.Metadata.Type)
            // are similar to System.Type, but represent types that live in the process being debugged.
            let lmrType = clrType.GetLmrType()

            let irisType = Utility.getIrisTypeForLmrType lmrType

            if irisType = IrisType.Invalid then
                // We don't know about this type.  Delegate to the C# Formatter to format the
                // type name.
                inspectionContext.GetTypeName(clrType, customTypeInfo, formatSpecifiers)
            else
                irisType.ToString()

        /// <summary>
        /// This method is called by the debug engine to populate the text representing the value
        /// of an expression.
        /// </summary>
        /// <param name="clrValue">The raw value to get the text for</param>
        /// <param name="inspectionContext">Context of the evaluation.  This contains options/flags
        /// to be used during compilation. It also contains the InspectionSession.  The inspection
        /// session is the object that provides lifetime management for our objects.  When the user
        /// steps or continues the process, the debug engine will dispose of the inspection session</param>
        /// <param name="formatSpecifiers"></param>
        /// <returns>The text representing the given value</returns>
        member this.GetValueString(clrValue : DkmClrValue, inspectionContext : DkmInspectionContext, formatSpecifiers : ReadOnlyCollection<string>) =
            let clrType = clrValue.Type
            if clrType = null then
                // This can be null in some error cases
                String.Empty
            else
                // Try to format the value.  If we can't format the value, delegate to the C# Formatter.
                match this.TryFormatValue(clrValue, inspectionContext) with
                | null -> clrValue.GetValueString(inspectionContext, formatSpecifiers)
                | x    -> x

        /// <summary>
        /// This method is called by the debug engine to get the raw string to show in the
        /// string/xml/html visualizer.
        /// </summary>
        /// <param name="clrValue">The raw value to get the text for</param>
        /// <param name="inspectionContext">Context of the evaluation.  This contains options/flags
        /// to be used during compilation. It also contains the InspectionSession.  The inspection
        /// session is the object that provides lifetime management for our objects.  When the user
        /// steps or continues the process, the debug engine will dispose of the inspection session</param>
        /// <returns>Raw underlying string</returns>
        member __.GetUnderlyingString(clrValue : DkmClrValue, inspectionContext : DkmInspectionContext) =
            // Get the raw string to show in the string/xml/html visualizer.
            // The C# behavior is good enough for our purposes.
            clrValue.GetUnderlyingString(inspectionContext)

        /// <summary>
        /// This method is called by the debug engine to determine if a value has an underlying
        /// string.  If so, the debugger will show a magnifying glass icon next to the value.  The
        /// user can then use it to select a text visualizer.
        /// </summary>
        /// <param name="clrValue">The raw value to get the text for</param>
        /// <param name="inspectionContext">Context of the evaluation.  This contains options/flags
        /// to be used during compilation. It also contains the InspectionSession.  The inspection
        /// session is the object that provides lifetime management for our objects.  When the user
        /// steps or continues the process, the debug engine will dispose of the inspection session</param>
        /// <returns></returns>
        member __.HasUnderlyingString(clrValue, inspectionContext) =
            // The C# behavior is good enough for our purposes.
            clrValue.HasUnderlyingString(inspectionContext)

    member private this.TryFormatValue(value : DkmClrValue, inspectionContext : DkmInspectionContext) =
        if value.ValueFlags.HasFlag(DkmClrValueFlags.Error) then
            // Error message.  Just show the error.
            match value.HostObjectValue with
            | :? string as x -> x
            | _ -> null
        else if value.IsNull then
            "<uninitialized>"
        else
            let lmrType = value.Type.GetLmrType()
            let irisType = Utility.getIrisTypeForLmrType lmrType
            if irisType = IrisType.Invalid then
                // We don't know how to format this value
                null
            else
                let radix = inspectionContext.Radix

                if irisType.IsArray then
                    let subrange = new SubRange(value.ArrayLowerBounds.First(), value.ArrayDimensions.First() - 1);
                    String.Format
                        (
                            "array[{0}..{1}] of {2}",
                            this.FormatInteger(subrange.From, radix),
                            this.FormatInteger(subrange.To, radix),
                            irisType.GetElementType()
                        )
                else
                    let hostObjectValue = value.HostObjectValue

                    if hostObjectValue <> null then
                        // If the value can be marshalled into the debugger process, HostObjectValue is the
                        // equivalent value in the debugger process.
                        match System.Type.GetTypeCode(hostObjectValue.GetType()) with
                        | TypeCode.Int32 ->
                            this.FormatInteger(unbox hostObjectValue, radix);
                        | TypeCode.Boolean ->
                            if (unbox hostObjectValue) then "true" else "false"
                        | TypeCode.String ->
                            this.FormatString(hostObjectValue.ToString(), inspectionContext.EvaluationFlags)
                        | _ -> null
                    else
                        null

    member private __.FormatInteger(value : int, radix : uint32) =
        if radix = 16u then "#" + value.ToString("x8") else value.ToString()

    member private __.FormatString(s : string, flags : DkmEvaluationFlags) =
        if flags.HasFlag(DkmEvaluationFlags.NoQuotes) then
            // No quotes - return the raw string.
            // If Iris handled escaping aside from quotes, we would still want to do escaping.
            s
        else
            // Escape special characters in the string and wrap in single quotes.
            let s = s.Replace("'", "''")
            "'" + s + "'"
