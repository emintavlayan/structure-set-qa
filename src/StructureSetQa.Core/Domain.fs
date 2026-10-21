namespace StructureSetQa

/// Contains the clinical vocabulary and geometry produced by read-only extraction.
module Domain =

    /// Represents the patient's anatomical side.
    type Side = Left | Right

    /// Represents a clinically identified anatomical structure.
    type Organ =
        | Rectum | Bladder | Prostate | SeminalVesicles
        | Kidney of Side | Lung of Side | Parotid of Side
        | SpinalCord | Brainstem | Heart | Liver | Spleen
        | Esophagus | Trachea | Sacrum
        | Vertebra of VertebralLevel
        | OtherOrgan of string

    /// Represents a vertebra or sacral segment, independent of slice number.
    and VertebralLevel =
        | Cervical of int | Thoracic of int | Lumbar of int | Sacral of int

    /// Represents a point in source patient XYZ coordinates (millimetres), without axis remapping.
    type PatientPoint = { X: float; Y: float; Z: float }

    /// Represents a source-coordinate-axis-aligned bounding box in millimetres.
    type PatientBounds = { Minimum: PatientPoint; Maximum: PatientPoint }

    /// Represents an observed contour's relationship to the scanned image grid.
    type ScanCoverage =
        | NotAssessed of reason: string
        | MeasuredGridClearances of (ScanEdge * float) list

    /// Represents a patient-anatomical boundary of the acquired CT grid.
    and ScanEdge =
        | AnteriorEdge | PosteriorEdge | LeftEdge | RightEdge
        | SuperiorEdge | InferiorEdge

    /// Represents whether the entire anatomical organ has been segmented.
    type AnatomicalCompleteness =
        | NotEstablished
        | SuspectedPartial of reason: string
        | ConfirmedPartial of reason: string
        | ClinicallyConfirmedComplete

    /// Represents a measured nonempty structure, not necessarily the whole organ.
    type ContourGeometry = {
        ObservedVolumeCc: float
        Center: PatientPoint
        MeshBounds: PatientBounds
        SeparateParts: int option
        Coverage: ScanCoverage
        Completeness: AnatomicalCompleteness
    }

    /// Represents the state of a named structure in the current structure set.
    type ContourObservation =
        | Missing
        | Empty
        | Measured of ContourGeometry
        | ExtractionFailed of message: string

    /// Represents one extracted structure without retaining adapter-specific objects.
    type StructureObservation = {
        Organ: Organ
        ActualId: string option
        Contour: ContourObservation
    }

    /// Represents the inspected CT grid envelope, not a complete body outline.
    type ScanObservation = {
        ImageId: string
        GridBounds: PatientBounds
        AxisAlignedWithPatient: bool
    }

    /// Represents all extracted anatomy available for one QA run.
    type AnatomySnapshot = {
        StructureSetId: string
        Scan: ScanObservation
        Structures: Map<Organ, StructureObservation>
    }

    /// Represents a choice of measured structure location.
    type LocationMeasure =
        | StructureCenterPoint
        | BoundingBoxCentre
        | SuperiorExtent
        | InferiorExtent

    /// Represents a qualitative anatomical direction relative to another structure.
    type RelativeDirection =
        | AnteriorTo | PosteriorTo | LeftOf | RightOf
        | SuperiorTo | InferiorTo

    /// Represents the coverage required before comparing two measured structure centers.
    type ComparisonCoverage =
        | AnyObservedExtent
        | BothClearOfGridEdges of minimumClearanceMm: float

    /// Represents one structure-local QA expectation.
    type IntrinsicRule =
        | MustBePresent
        | MaximumSeparateParts of count: int
        | ReportGridEdgeProximity of marginMm: float

    /// Represents a spatial relationship between this organ and a second organ.
    type RelativeRule = {
        Other: Organ
        Direction: RelativeDirection
        Measure: LocationMeasure
        MinimumDifferenceMm: float
        Coverage: ComparisonCoverage
    }

    /// Represents a vertebral-level window for a structure center or extent.
    type VertebralWindow = { From: VertebralLevel; Through: VertebralLevel; MarginMm: float }

    /// Represents rules that require individually identified vertebrae.
    type VertebralRule =
        | CentreWithin of VertebralWindow
        | PresentBetween of VertebralWindow

    /// Represents the three families of checks that may be attached to any organ.
    type StructureRule =
        | Intrinsic of IntrinsicRule
        | Relative of RelativeRule
        | Vertebral of VertebralRule

    /// Represents the clinical priority of a failed rule.
    type Severity = Information | Warning | Critical

    /// Represents one stable, identifiable, configurable rule.
    type RuleDefinition = {
        Id: string
        Severity: Severity
        Rule: StructureRule
    }

    /// Represents a structure identity, accepted source IDs, and its anatomical rules.
    type StructureDefinition = {
        Organ: Organ
        SourceIds: string list
        Rules: RuleDefinition list
    }

    /// Represents the clinical protocol's expected anatomy and QA knowledge.
    type QaKnowledge = { Version: int; Structures: StructureDefinition list }

    /// Represents a typed fact explaining why a rule passed or raised a warning.
    type Evidence =
        | ActualComponentCount of int
        | ObservedDifferenceMm of float
        | ScanClearanceMm of float
        | BoundaryEdges of ScanEdge list
        | ObservedStructureVolumeCc of float

    /// Represents the result of checking one anatomical expectation.
    type RuleOutcome =
        | Passed of Evidence list
        | Flagged of Severity * Evidence list
        | NotEvaluated of reason: string
        | EvaluationError of message: string

    /// Represents an auditable QA finding for one structure and one named rule.
    type RuleFinding = {
        Organ: Organ
        RuleId: string
        Outcome: RuleOutcome
    }

    /// Represents initial or extended structure-set checks, without plan or dose QA.
    type ReviewStage =
        | InitialContourReview
        | ExtendedStructureReview

    /// Represents the complete result of one clinical QA execution.
    type QaReport = {
        Stage: ReviewStage
        StructureSetId: string
        Findings: RuleFinding list
    }
