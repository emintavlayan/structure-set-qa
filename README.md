# structure-set-qa

A vendor-independent, anatomy-aware **structure-set quality assurance** engine for radiotherapy. It evaluates contour geometry and topology, CT coverage, bilateral and cross-organ anatomical relationships, and vertebral-level position plausibility.

It is **only structure-set QA**. It does not inspect plan quality, dose, or beam arrangements.

## Clinical workflow

- **Initial contour review:** run before planning on an automatically generated or manually prepared structure set.
- **Extended structure review (future):** run again after additional structures such as CTV and PTV have been created. Only structure-set checks are added.
- **Population-level reference models (future):** analyze clinic-approved structure sets after permission, with proper treatment-site categorization and partial-scan safeguards.

The current integration is an **Eclipse ESAPI read-only plug-in script adapter**, with actual plug-in entry point and UI still to be developed. The core does not depend on Eclipse or any auto-contouring vendor.

## Run locally without Eclipse

Clone the repo, then run the vendor-independent F# tests and command-line tools:

```powershell
dotnet test tests/StructureSetQa.Core.Tests/StructureSetQa.Core.Tests.fsproj
dotnet run --project src/StructureSetQa.Cli -- validate
dotnet run --project src/StructureSetQa.Cli -- inventory samples/structure-ids.txt
dotnet run --project src/StructureSetQa.Cli -- demo
```

The `demo` command uses synthetic organ centers and deliberate contour anomalies. It does **not** connect to an active treatment planning system.

See [local setup and CLI guide](docs/LOCAL.md) for prerequisites and Windows/Neovim development steps.

## Clinician-editable QA rules

| File | Contains |
| --- | --- |
| [`rules/structure-ids.yaml`](rules/structure-ids.yaml) | Accepted IDs and aliases, laterality tokens, excluded PRV-style modifiers |
| [`rules/geometry.yaml`](rules/geometry.yaml) | Connected components and CT-boundary proximity for each structure |
| [`rules/relationships.yaml`](rules/relationships.yaml) | Kidney laterality, organ ordering, heart between lungs, liver versus spleen |
| [`rules/vertebrae.yaml`](rules/vertebrae.yaml) | Nearest labeled vertebral-center expectations |

**Start with [Clinical rulebook editing guide](docs/RULES.md).** These rulebooks are examples and the numerical clinical expectations are not validated thresholds.

The application parses all four YAML files into strongly typed F# domain rules. Invalid syntax or unrecognized keywords produce a configuration error. It inventories **all** structure IDs first, recording recognized, unrecognized, excluded, ambiguous, and duplicate identities. Individual rules then operate only when the required measured structures exist.

The identity resolver ignores casing, ordinary separators and accents, supports leading and trailing left/right suffixes, and excludes PRV-style modified contours from ordinary OAR identities. It recognizes vertebral labels such as `T9`, `TH9`, and `Vertebra_T9`.

The closest-vertebra rule uses only `abs(organ.Center.Z - vertebra.Center.Z)` and requires locally continuous vertebral labeling bracketing the organ center. It never invents missing vertebrae. **Slice-wise cross-sectional anatomy QA is future work.**

## Implementation

```
src/
  StructureSetQa.Core/
    Domain.fs          typed rules, observations and findings
    Names.fs           source ID resolver and structure-set inventory
    Configuration.fs   YAML parser and validation
    Knowledge.fs       validated configuration entry point
    Analysis.fs        pure anatomical rule evaluation
  StructureSetQa.Esapi/
    EsapiReader.fs     read-only structure/CT extraction
    Workflow.fs        load rulebooks, inventory, evaluate
tests/
  StructureSetQa.Core.Tests/
rules/
docs/
```

Run vendor-independent tests:

```sh
dotnet test tests/StructureSetQa.Core.Tests/StructureSetQa.Core.Tests.fsproj
```

The core targets `.NET Standard 2.0`. The synthetic tests target `.NET 8`; GitHub Actions runs them without accessing any patient data.

For the ESAPI adapter, provide the directory containing the four YAML rulebooks:

```fsharp
match StructureSetQa.Workflow.runInitialReview rulesDirectory scriptContext with
| Ok report -> // render findings
| Error message -> // configuration or extraction failure
```

## Coordinates and coverage

We retain the original ESAPI-returned **DICOM patient XYZ** coordinates without transformation (X left, Y posterior, Z superior). A future spine-relative position is a separate derived measurement; the original positions remain unchanged.

CT grid-face proximity provides evidence of **possible scan truncation**, not proof that the organ is incomplete. Checks depending on a whole-organ center return `NotEvaluated` when relevant scan boundaries are near the contour. Unknown labels or incomplete vertebral coverage also produce `NotEvaluated`, not a fabricated pass.

## Status and limitations

- Clinician-editable rulebooks, YAML parser, typed rule model, naming resolver, source inventory, and pure analysis are implemented in source.
- The initial ESAPI adapter reads structures and CT geometry; the entry point, UI, and installed-version compilation are not yet complete.
- Connected-component count is used if the installed ESAPI interface exposes the method; otherwise the check is not evaluated.
- The vertebral check currently compares **whole-contour centers**, not cross-sectional areas or slice-wise positions.
- Source names and example anatomical ranges require local clinical verification; no historical data is included.
- The repository is a **research/development prototype**, not validated clinical software.
