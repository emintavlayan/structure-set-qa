# structure-set-qa

Anatomy-aware, read-only QA of Eclipse **structure sets**, including individual contours, CT truncation evidence, and spatial relationships between organs.

## Clinical workflow

1. **Before planning:** dosimetrist opens the Siemens-generated CT structure set; the script measures organ geometry, scan-edge proximity, separate-part count (if exposed by ESAPI), and simple anatomical ordering.
2. **After planning (planned):** rerun baseline checks and add CTV/PTV, plan, and dose-related rules.
3. **Statistical learning (planned):** characterize full or partial structures using clinic-approved historical data, including spine-relative features and conditional reference ranges.

The QA report is **advisory**, not approval or automatic contour correction. This is a research/development prototype, **not validated for clinical decision-making**.

## Layout

- `src/StructureSetQa.Core/Domain.fs` — typed anatomy, rules, findings
- `src/StructureSetQa.Core/Knowledge.fs` — initial fixed-ID Siemens rule catalog
- `src/StructureSetQa.Core/Analysis.fs` — pure QA engine
- `src/StructureSetQa.Esapi/EsapiReader.fs` — read-only ScriptContext adapter
- `src/StructureSetQa.Esapi/Workflow.fs` — pre-planning workflow
- `tests/StructureSetQa.Core.Tests/AnalysisTests.fs` — synthetic tests

## Development

```sh
dotnet test tests/StructureSetQa.Core.Tests/StructureSetQa.Core.Tests.fsproj
```

The core targets .NET Standard 2.0; pure tests target .NET 8. The ESAPI boundary requires vendor assemblies and a configured clinical development machine.

## Modeling decisions

- Patient coordinates: **X anterior, Y left, Z superior**, millimetres; adapter converts DICOM LPS.
- Structure definitions hold fixed source IDs and nested intrinsic, relative, vertebral rule definitions.
- Measurements remain distinct from anatomical interpretations.
- Contact with a CT grid boundary is possible truncation evidence, not proof.
- Unknown vertebral mapping produces NotEvaluated.
- No Eclipse modifications, patient exports, or patient datasets.

## Current limitations

- Actual Siemens structure IDs and clinical margins require local confirmation.
- Separate-part ESAPI method availability depends on vendor version.
- Oblique CT grid-face clearance is currently not evaluated.
- Vertebral rules are modeled but not executed until labeled landmarks exist.
- This code is not yet compiled or clinically validated here.
- YAML and post-planning checks are planned.
