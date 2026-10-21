namespace StructureSetQa

/// Defines vendor-independent anatomy, contour observations, and typed QA knowledge.
module Domain =

    /// Represents anatomical laterality.
    type Side = Left | Right

    /// Represents an individually labeled vertebral or sacral level.
    type VertebralLevel =
        | Cervical of int
        | Thoracic of int
        | Lumbar of int
        | Sacral of int

    /// Represents the anatomical identity of a structure.
    type Organ =
        | Rectum | Bladder | Prostate | SeminalVesicles | Uterus
        | Kidney of Side | Lung of Side | Parotid of Side | FemoralHead of Side
        | SpinalCord | Brainstem | Heart | Liver | Spleen | Esophagus | Trachea
        | Sacrum | Vertebra of VertebralLevel
        | OtherOrgan of string

    /// Represents a source patient XYZ point in millimetres, without coordinate conversion.
    type PatientPoint = { X: float; Y: float; Z: float }

    /// Represents a source-coordinate-aligned physical bounding box.
    type PatientBounds = { Minimum: PatientPoint; Maximum: PatientPoint }

    /// Represents a named patient-anatomical boundary of a CT volume.
    type ScanEdge =
        | AnteriorEdge | PosteriorEdge | LeftEdge | RightEdge
        | SuperiorEdge | InferiorEdge

    /// Represents available geometrical evidence about CT scan boundaries.
    type ScanCoverage =
        | NotAssessed of reason: string
        | MeasuredGridClearances of (ScanEdge * float) list

    /// Represents an interpretation of whether a full organ has been delineated.
    type AnatomicalCompleteness =
        | NotEstablished
        | SuspectedPartial of reason: string
        | ConfirmedPartial of reason: string
        | ClinicallyConfirmedComplete

    /// Represents measurements of a nonempty contour, even when anatomically partial.
    type ContourGeometry = {
        ObservedVolumeCc: float
        Center: PatientPoint
        MeshBounds: PatientBounds
        SeparateParts: int option
        Coverage: ScanCoverage
        Completeness: AnatomicalCompleteness
    }

    /// Represents the measured or unavailable state of a contour.
    type ContourObservation =
        | Missing
        | Empty
        | Measured of ContourGeometry
        | ExtractionFailed of message: string

    /// Represents a recognized anatomical structure in a structure set.
    type StructureObservation = {
        Organ: Organ
        ActualId: string option
        Contour: ContourObservation
    }

    /// Represents name-matching outcomes for the entire structure-set inventory.
    type NameMatch =
        | Recognized of Organ
        | Unrecognized
        | ExcludedModifier of string
        | Ambiguous of Organ list

    /// Represents the resolution of one actual source structure identifier.
    type InventoryItem = { ActualId: string; Match: NameMatch }

    /// Represents what the structure set contains before any QA rule executes.
    type StructureInventory = {
        Items: InventoryItem list
        DuplicateOrgans: (Organ * string list) list
    }

    /// Represents scanned CT limits in source millimetre coordinates.
    type ScanObservation = {
        ImageId: string
        GridBounds: PatientBounds
        AxisAlignedWithPatient: bool
    }

    /// Represents an immutable snapshot of the available structure set.
    type AnatomySnapshot = {
        StructureSetId: string
        Scan: ScanObservation
        Inventory: StructureInventory
        Structures: Map<Organ, StructureObservation>
    }

    /// Represents a measurement used to compare anatomical positions.
    type LocationMeasure =
        | StructureCenterPoint
        | BoundingBoxCentre
        | SuperiorExtent
        | InferiorExtent

    /// Represents an anatomical relation in original ESAPI XYZ coordinates.
    type RelativeDirection =
        | AnteriorTo | PosteriorTo | LeftOf | RightOf
        | SuperiorTo | InferiorTo

    /// Represents the required CT coverage for comparing whole-organ positions.
    type ComparisonCoverage =
        | AnyObservedExtent
        | BothClearOfGridEdges of minimumClearanceMm: float

    /// Represents intrinsic measurements that apply to a single contour.
    type IntrinsicRule =
        | MustBePresent
        | MaximumSeparateParts of count: int
        | ReportGridEdgeProximity of marginMm: float

    /// Represents a center's permitted location among identified vertebral levels.
    type VertebralRule =
        | NearestVertebraAllowed of allowed: VertebralLevel list

    /// Represents rules that need only one structure and optionally vertebral landmarks.
    type StructureRule =
        | Geometry of IntrinsicRule
        | Vertebral of VertebralRule

    /// Represents a pairwise center or extent position comparison.
    type RelativeRule = {
        Subject: Organ
        Reference: Organ
        Direction: RelativeDirection
        Measure: LocationMeasure
        MinimumDifferenceMm: float
        Coverage: ComparisonCoverage
    }

    /// Represents an organ center expected between two other organ centers on one axis.
    type BetweenRule = {
        Subject: Organ
        First: Organ
        Second: Organ
        Axis: char
        MarginMm: float
        Coverage: ComparisonCoverage
    }

    /// Represents rules evaluated against relationships in the whole structure set.
    type StructureSetRule =
        | RelativePosition of RelativeRule
        | BetweenCentres of BetweenRule

    /// Represents the clinical severity of a rule violation.
    type Severity = Information | Warning | Critical

    /// Represents an identified structure-level rule with its configured severity.
    type StructureRuleDefinition = { Id: string; Severity: Severity; Rule: StructureRule }

    /// Represents an identified structure-set rule with its configured severity.
    type StructureSetRuleDefinition = { Id: string; Severity: Severity; Rule: StructureSetRule }

    /// Represents one anatomical identity with human-editable accepted names and structure rules.
    type StructureDefinition = {
        Organ: Organ
        Aliases: string list
        Rules: StructureRuleDefinition list
    }

    /// Represents one source-name exclusion that must not be mistaken for an anatomical OAR.
    type NamingConvention = {
        LeftTokens: string list
        RightTokens: string list
        ExcludedModifierTokens: string list
        VertebraPrefixes: string list
    }

    /// Represents the full validated naming configuration.
    type NamingDictionary = {
        Convention: NamingConvention
        Entries: (Organ * string list) list
    }

    /// Represents all typed clinical knowledge loaded from the four rulebooks.
    type QaKnowledge = {
        Version: int
        Naming: NamingDictionary
        Structures: StructureDefinition list
        StructureSetRules: StructureSetRuleDefinition list
    }

    /// Represents objective evidence used to explain one QA result.
    type Evidence =
        | ActualComponentCount of int
        | ObservedDifferenceMm of float
        | ScanClearanceMm of float
        | BoundaryEdges of ScanEdge list
        | ObservedStructureVolumeCc of float
        | NearestVertebra of VertebralLevel * float

    /// Represents a non-destructive structure-set QA decision.
    type RuleOutcome =
        | Passed of Evidence list
        | Flagged of Severity * Evidence list
        | NotEvaluated of reason: string
        | EvaluationError of message: string

    /// Represents one auditable rule evaluation and its involved anatomical identity.
    type RuleFinding = {
        Subject: Organ
        RuleId: string
        Outcome: RuleOutcome
    }

    /// Represents initial or extended structure-set review, never a dose/plan review.
    type ReviewStage = InitialContourReview | ExtendedStructureReview

    /// Represents a complete structure-set QA result and its prior inventory.
    type QaReport = {
        Stage: ReviewStage
        StructureSetId: string
        Inventory: StructureInventory
        Findings: RuleFinding list
    }
