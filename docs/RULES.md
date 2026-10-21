# Editing anatomical QA rules

This guide is intended for medical physicists, dosimetrists, oncologists, and other clinicians who do not write F#.

The application checks **the whole structure set**. It never corrects a contour and never performs plan or dose QA.

## Before changing a rule

1. Make a branch and edit a YAML file under `rules/`. No C# or F# knowledge is required.
2. Use an existing example. Preserve indentation (two spaces) and the `version: 1` line.
3. Give every anatomical rule a unique `id` that will appear in the QA report.
4. Open a pull request. Another clinical reviewer should examine any change in anatomy, margin, or severity.
5. Run the pure unit tests. In GitHub, the **.NET core tests** workflow checks parsing and tests without a treatment planning system.

Do not put patient identifiers, screenshots, DICOM data, or clinical exports into the repository.

## 1. `structure-ids.yaml`: Which structure is this?

Example:

```yaml
structures:
  rectum: [Rectum, Rect, Rektum]
  kidney_left: [Kidney, Ren, Nyre]
  kidney_right: [Kidney, Ren, Nyre]
```

A word `Rektum` resolves to the anatomical identity `rectum`.

Left/right labels are assembled from organ aliases plus configured side tokens. For example `Kidney_L`, `LEFT KIDNEY`, `KidneyRt`, `Nyre H`, and `Ren Sin`. Separators, case, and common accented characters are normalized. The resolver matches **complete names**, not arbitrary substrings.

A structure containing an excluded modifier as a separate token, e.g. `Kidney_L_PRV`, is excluded from the normal kidney identity. An unexpected or ambiguous name remains visible in the inventory and is not silently assigned.

Automatically labeled vertebrae such as `T09`, `TH9`, `VB_T9`, and `Vertebra_T9` resolve to `T9`. Only recognized, unambiguous anatomical identities are measured for the corresponding rules.

All names shown in the files are **suggested starting aliases**, not an assertion about any manufacturer's guaranteed export naming. Confirm actual IDs from a local structure-set inventory.

## 2. `geometry.yaml`: One contour at a time

```yaml
version: 1
defaults:
  scan_edge_margin_mm: 3
structures:
  rectum:
    max_connected_parts: 1
```

`max_connected_parts` asks whether a contour is unusually disconnected in 3D; the method must be available in the current adapter. If it is not available, the report says **Not evaluated**.

`scan_edge_margin_mm` measures proximity to the acquired CT boundary. This signals **possible scan truncation**, never proof of a contouring error.

No organ is required to exist merely because it appears in this file. A missing organ gives **Not evaluated**. An explicitly required-structure capability can be added after the team defines the treatment-site protocols.

## 3. `relationships.yaml`: Anatomy between structures

```yaml
version: 1
rules:
  - id: rectum.posterior-to-bladder
    kind: relative_position
    subject: rectum
    reference: bladder
    direction: posterior_to
    minimum_mm: 5
    coverage: complete_candidate
    severity: warning
```

This checks whether the rectum's measured center is sufficiently posterior to the bladder's center. Relative direction choices: `left_of`, `right_of`, `anterior_to`, `posterior_to`, `superior_to`, `inferior_to`.

A second kind checks whether a center lies between the centers of two other structures:

```yaml
  - id: heart.between-lungs
    kind: between_centres
    subject: heart
    first: lung_left
    second: lung_right
    axis: x
    margin_mm: 0
    coverage: complete_candidate
    severity: information
```

The margin is in millimetres. `x` means patient left/right, `y` anterior/posterior, and `z` superior/inferior. The source ESAPI/DICOM XYZ coordinates are used **without remapping**: X increases leftward, Y posteriorly, Z superiorly.

`coverage: observed` permits center comparisons even for potentially partial contours.
`coverage: complete_candidate` suspends the rule if a contour lies within 3 mm of the CT boundary (a screening threshold, not proof of completeness).

A rule that needs a missing, empty, ambiguous, or unusable structure returns **Not evaluated**.

## 4. `vertebrae.yaml`: Center closest to a labeled vertebra

```yaml
version: 1
rules:
  - id: kidney-left.vertebral-center
    subject: kidney_left
    allowed_levels: [T11, T12, L1, L2, L3, L4]
    coverage: complete_candidate
    severity: information
```

The engine reads organ and vertebral centers and chooses the labeled vertebral center with the smallest **absolute Z difference**, `abs(organ.Z - vertebra.Z)`. It does not use full 3D distance, which is strongly affected by lateral organ placement.

Two neighboring vertebrae must bracket the organ center and their labels must be consecutive. Missing labels, partial CT coverage, or an organ outside the labeled scan window give **Not evaluated**, rather than a fabricated anatomical level.

**These example anatomical windows have not been validated.** They are broad screening seeds, not delineation definitions or mandatory clinical ranges. An uncommon normal variant may trigger a review finding.

**Cross-sectional QA is a next layer:** a labeled vertebra defines a relevant axial level; then per-slice organ bounding boxes, centers, and presence/absence can be compared. The current version only checks *overall contour center versus vertebral centers*. Do not interpret it as a completed slice-by-slice atlas.

## Rule outcomes

| Outcome | Meaning |
| --- | --- |
| Passed | Available measurements agree with the rule |
| Flagged | Measurements differ from an authored clinical expectation |
| Not evaluated | Prerequisite anatomy, coverage, or geometry was unavailable |
| Evaluation error | Extraction or calculation failed |

Flags are review prompts. They are not diagnostic conclusions, clinical approvals, or automatic corrections.

## Contribution protocol

A new clinical rule should include a brief explanation in its pull-request description: anatomical rationale, intended population, expected exceptions, and proposed priority. Threshold changes should be reviewed on synthetic cases before any approved retrospective evaluation.

The rule engine and examples deliberately remain small. Expand the YAML knowledge collaboratively; keep hardcoded F# clinical assumptions to a minimum.
