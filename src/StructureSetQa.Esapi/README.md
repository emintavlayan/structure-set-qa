# ESAPI integration boundary

EsapiReader.fs and Workflow.fs are intended for inclusion in an Eclipse-compatible F# application/plugin project targeting the .NET Framework and ESAPI API versions installed at the clinic.

Compile order:
1. Reference StructureSetQa.Core.
2. Compile EsapiReader.fs.
3. Compile Workflow.fs.

References: installed ESAPI Model.API/Types assemblies, WPF PresentationCore and WindowsBase, and FsToolkit.ErrorHandling.

Example invocation:

    match StructureSetQa.Workflow.runBeforePlanning context with
    | Ok report -> // render findings
    | Error message -> // display startup failure

No call to BeginModifications is made. The script reads only ScriptContext.StructureSet and associated CT image.

**Clinical integration is not yet compiled or validated.** The Siemens IDs in Knowledge.fs are examples and must be checked against actual CT-generated structure IDs. GetNumberOfSeparateParts is reflected because method availability varies by ESAPI release. Its absence reports NotEvaluated.
