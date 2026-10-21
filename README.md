# structure-set-qa

Anatomy-aware, read-only quality assurance of a **complete radiotherapy structure set**.

The project evaluates individual contours **and relationships between structures**, including topology, relative anatomical position, CT scan coverage, and (later) vertebral-level comparisons and cohort-derived anatomical reference distributions.

This project remains exclusively **structure-set QA**. It does not assess treatment plan quality or calculate dosimetric metrics.

## Clinical workflow

1. **Initial contour review:** before treatment planning, inspect the available structure set and flag suspicious anatomy, isolated contour components, and possible scan truncation.
2. **Extended structure review (planned):** run structure-set QA again after additional structures have been created, including CTV and PTV. This stage adds structure/target geometry checks; it does not analyze the treatment plan or dose.
3. **Population learning (planned):** compare appropriately categorized structures against anatomical distributions learned from clinic-approved reference data, after the necessary permissions.

Both reviews operate on **the structure set**, not on treatment-plan data.

## Architecture

- `src/StructureSetQa.Core/Domain.fs` — types describing structures, measurements, QA rule definitions, and findings
- `src/StructureSetQa.Core/Knowledge.fs` — initial provisional structure IDs and typed anatomical rule catalogue
- `src/StructureSetQa.Core/Analysis.fs` — pure, vendor-independent rule evaluation
- `src/StructureSetQa.Esapi/EsapiReader.fs` — current read-only ESAPI structure-set and CT adapter
- `src/StructureSetQa.Esapi/Workflow.fs` — current initial-review orchestration
- `tests/StructureSetQa.Core.Tests/AnalysisTests.fs` — synthetic tests, without patient data

The domain and rule engine do not depend on Eclipse or any auto-contouring vendor. **The current integration** targets an Eclipse **ESAPI plug-in script**, using the active `ScriptContext.StructureSet`. The adapter is under development; a launchable plug-in entry point and report UI are not yet present.

## Coordinate conventions

Preserve the original **XYZ millimetre coordinates supplied by ESAPI**, without introducing a custom axis order or changing signs:

| Axis | Anatomical dimension | Increasing DICOM patient coordinate |
|---|---|---|
| X | Left–right | Left |
| Y | Anterior–posterior | Posterior |
| Z | Inferior–superior | Superior |

The implementation uses **ESAPI-returned DICOM patient coordinates**, which differ from the user-defined planning coordinate display in the Eclipse UI. Position comparisons and scan-boundary checks use source XYZ directly. See [Varian ESAPI coordinate documentation](https://docs.developer.varian.com/articles/15.6/05_Eclipse_Scripting_API_Object_Model.html) and [DICOM PS3.3](https://dicom.nema.org/medical/DICOM/current/output/chtml/part03/sect_C.7.6.2.html).

There is no X/Y swap, AP sign inversion, spine-relative origin, or automatic coordinate normalization in the first implementation. Spine-relative measurements may be added later as **derived features**, without altering original positions.

## Development

```sh
dotnet test tests/StructureSetQa.Core.Tests/StructureSetQa.Core.Tests.fsproj
```

The pure core targets .NET Standard 2.0 and the tests target .NET 8. The ESAPI adapter requires the compatible vendor assemblies and .NET Framework configuration on a development workstation.

## Current status and limitations

- Source is committed, but compilation and ESAPI integration have **not** been executed in this environment.
- Example source structure IDs and clinical thresholds need local confirmation; they are not yet validated clinical rules.
- Component count uses ESAPI's method if available in the installed version; otherwise the check is not evaluated.
- Scan-boundary proximity provides possible truncation evidence, not proof of contour incompleteness.
- Oblique image grid-face clearance is not yet evaluated.
- Vertebral rules have type definitions but no labeled-vertebra evaluation yet.
- Extended CTV/PTV rules, editable YAML knowledge, and population learning are future work.
- The code reads data only; it never changes or approves contours.

**Research/development prototype — not clinically validated.**
