# Current adapter: Eclipse ESAPI plug-in script

This is an **implementation detail** of the vendor-independent `structure-set-qa` project. The domain remains exclusively structure-set QA.

The read-only adapter uses the active `ScriptContext.StructureSet` and its associated image and structures. It does not inspect dose or treatment-plan parameters.

## Source integration

Compile order:

1. Reference `StructureSetQa.Core`.
2. Compile `EsapiReader.fs`.
3. Compile `Workflow.fs`.

Reference the installed ESAPI API/Types assemblies, the WPF geometry assemblies, and `FsToolkit.ErrorHandling`. Use the matching .NET Framework and ESAPI version for the clinic.

Current entry-point example:

```fsharp
match StructureSetQa.Workflow.runInitialReview context with
| Ok report -> // display structure-set findings
| Error error -> // display read/extraction failure
```

The actual ESAPI plug-in entry point, build configuration and report UI still need implementation.

## Coordinates

The adapter copies `VVector.x`, `VVector.y`, `VVector.z` **unchanged** into the core `PatientPoint.X/Y/Z`. Both structure-mesh and image positions remain in ESAPI's original DICOM patient millimetre frame: positive X left, positive Y posterior, positive Z superior. No axis reorder, sign inversion, or synthetic origin is used.

The Eclipse UI planning coordinate display is a different representation; the integration deliberately uses the API-returned coordinates directly.

## Constraints

- Read-only: no `BeginModifications()`.
- The predefined IDs in `Knowledge.fs` are initial examples; verify against the active structure set.
- `GetNumberOfSeparateParts` is accessed only when available in the installed ESAPI version.
- The current review examines existing contours. A subsequent structure-set review will be able to add CTV/PTV geometry checks, without becoming a plan QA tool.
- Compilation and clinical validation remain outstanding.
