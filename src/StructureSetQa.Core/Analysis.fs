namespace StructureSetQa

open Domain

/// Evaluates structural and relational QA rules against one immutable anatomy snapshot.
module Analysis =

    /// Gets the current contour for an anatomical identity.
    let observation (snapshot: AnatomySnapshot) organ =
        snapshot.Structures
        |> Map.tryFind organ
        |> Option.map (fun item -> item.Contour)
        |> Option.defaultValue Missing

    /// Reads the minimum measured clearance from the CT grid.
    let minimumGridClearance geometry =
        match geometry.Coverage with
        | NotAssessed reason -> Error reason
        | MeasuredGridClearances [] -> Error "CT boundary clearances are unavailable."
        | MeasuredGridClearances values -> values |> List.map snd |> List.min |> Ok

    /// Checks whether one contour has sufficient scan-boundary clearance.
    let checkSingleCoverage requirement geometry =
        match requirement with
        | AnyObservedExtent -> Ok ()
        | BothClearOfGridEdges margin ->
            minimumGridClearance geometry
            |> Result.bind (fun value ->
                if value > margin then Ok ()
                else Error "Contour approaches the CT boundary; whole-organ center may be biased.")

    /// Checks that every contour used in a relationship meets its coverage prerequisite.
    let checkCoverage requirement geometries =
        geometries
        |> List.map (checkSingleCoverage requirement)
        |> List.tryPick (function Error reason -> Some reason | Ok () -> None)
        |> function Some reason -> Error reason | None -> Ok ()

    /// Gets the requested structure location using original source XYZ coordinates.
    let measuredLocation measure geometry =
        match measure with
        | StructureCenterPoint -> geometry.Center
        | BoundingBoxCentre ->
            { X = (geometry.MeshBounds.Minimum.X + geometry.MeshBounds.Maximum.X) / 2.0
              Y = (geometry.MeshBounds.Minimum.Y + geometry.MeshBounds.Maximum.Y) / 2.0
              Z = (geometry.MeshBounds.Minimum.Z + geometry.MeshBounds.Maximum.Z) / 2.0 }
        | SuperiorExtent -> { geometry.Center with Z = geometry.MeshBounds.Maximum.Z }
        | InferiorExtent -> { geometry.Center with Z = geometry.MeshBounds.Minimum.Z }

    /// Calculates a signed anatomical difference; positive indicates the expected direction.
    let signedDifference direction first second =
        match direction with
        | AnteriorTo -> second.Y - first.Y
        | PosteriorTo -> first.Y - second.Y
        | LeftOf -> first.X - second.X
        | RightOf -> second.X - first.X
        | SuperiorTo -> first.Z - second.Z
        | InferiorTo -> second.Z - first.Z

    /// Evaluates topology and observed CT grid-edge proximity for one structure.
    let evaluateIntrinsic contour rule =
        match rule, contour with
        | MustBePresent, Missing
        | MustBePresent, Empty -> Flagged (Warning, [])
        | MustBePresent, Measured value -> Passed [ObservedStructureVolumeCc value.ObservedVolumeCc]
        | _, Missing -> NotEvaluated "Structure was not found in this structure set."
        | _, Empty -> NotEvaluated "Structure exists but has no contour."
        | _, ExtractionFailed message -> EvaluationError message
        | MaximumSeparateParts maximum, Measured geometry ->
            match geometry.SeparateParts with
            | None -> NotEvaluated "A 3D connected-component count is unavailable."
            | Some count when count < 1 -> EvaluationError "Invalid connected-component count."
            | Some count when count <= maximum -> Passed [ActualComponentCount count]
            | Some count -> Flagged (Warning, [ActualComponentCount count])
        | ReportGridEdgeProximity margin, Measured geometry ->
            match geometry.Coverage with
            | NotAssessed reason -> NotEvaluated reason
            | MeasuredGridClearances [] -> NotEvaluated "No grid-face distances were measured."
            | MeasuredGridClearances distances ->
                let close = distances |> List.filter (fun (_, distance) -> distance <= margin)
                if List.isEmpty close then
                    let smallest = distances |> List.map snd |> List.min
                    Passed [ScanClearanceMm smallest]
                else Flagged (Information, [BoundaryEdges (close |> List.map fst)])

    /// Evaluates the relationship of two existing structures.
    let evaluateRelative (snapshot: AnatomySnapshot) rule =
        match observation snapshot rule.Subject, observation snapshot rule.Reference with
        | Measured subject, Measured reference ->
            match checkCoverage rule.Coverage [subject; reference] with
            | Error reason -> NotEvaluated reason
            | Ok () ->
                let first = measuredLocation rule.Measure subject
                let second = measuredLocation rule.Measure reference
                let delta = signedDifference rule.Direction first second
                if delta >= rule.MinimumDifferenceMm then Passed [ObservedDifferenceMm delta]
                else Flagged (Warning, [ObservedDifferenceMm delta])
        | ExtractionFailed reason, _ -> EvaluationError reason
        | _, ExtractionFailed reason -> EvaluationError reason
        | _ -> NotEvaluated "Both structures are required but at least one is absent or empty."

    /// Reads a source coordinate for a named X, Y, or Z axis.
    let coordinate axis location =
        match axis with
        | 'X' -> location.X
        | 'Y' -> location.Y
        | 'Z' -> location.Z
        | _ -> nan

    /// Evaluates whether one structure center is between two reference centers.
    let evaluateBetween (snapshot: AnatomySnapshot) rule =
        match observation snapshot rule.Subject,
              observation snapshot rule.First,
              observation snapshot rule.Second with
        | Measured subject, Measured first, Measured second ->
            match checkCoverage rule.Coverage [subject; first; second] with
            | Error reason -> NotEvaluated reason
            | Ok () ->
                let value = coordinate rule.Axis subject.Center
                let a = coordinate rule.Axis first.Center
                let b = coordinate rule.Axis second.Center
                let lower = min a b + rule.MarginMm
                let upper = max a b - rule.MarginMm
                if lower > upper then NotEvaluated "The two reference centers do not provide enough separation."
                else
                    let signedMargin = min (value - lower) (upper - value)
                    if signedMargin >= 0.0 then Passed [ObservedDifferenceMm signedMargin]
                    else Flagged (Warning, [ObservedDifferenceMm signedMargin])
        | ExtractionFailed reason, _, _
        | _, ExtractionFailed reason, _
        | _, _, ExtractionFailed reason -> EvaluationError reason
        | _ -> NotEvaluated "Three measured contours are needed for a between-centers rule."

    /// Orders labeled vertebrae from superior to inferior without using any CT slice indices.
    let vertebraOrdinal =
        function
        | Cervical number -> number
        | Thoracic number -> 7 + number
        | Lumbar number -> 19 + number
        | Sacral number -> 24 + number

    /// Collects centers of the vertebrae actually found in the current structure set.
    let vertebralCentres snapshot =
        snapshot.Structures
        |> Map.toList
        |> List.choose (fun (organ, item) ->
            match organ, item.Contour with
            | Vertebra level, Measured geometry -> Some (level, geometry.Center.Z)
            | _ -> None)
        |> List.sortBy snd

    /// Checks that the target is bracketed by nearby, consecutively labeled vertebrae.
    let comparableVertebrae z vertebrae =
        if List.length vertebrae < 2 then
            Error "At least two labeled vertebrae are required."
        else
            let lower = vertebrae |> List.filter (fun (_, center) -> center <= z) |> List.tryLast
            let upper = vertebrae |> List.tryFind (fun (_, center) -> center >= z)
            match lower, upper with
            | Some (lowerLevel, _), Some (upperLevel, _) ->
                let gap = abs (vertebraOrdinal upperLevel - vertebraOrdinal lowerLevel)
                if gap > 1 then Error "The vertebral labels bracketing the organ contain a gap."
                else Ok vertebrae
            | _ -> Error "Organ center lies outside the observed labeled vertebral range."

    /// Evaluates the nearest vertebral center in superior-inferior distance.
    let evaluateVertebral snapshot subject (NearestVertebraAllowed (levels, requiredCoverage)) =
        match observation snapshot subject with
        | Missing -> NotEvaluated "The structure was not found."
        | Empty -> NotEvaluated "The structure has no contour."
        | ExtractionFailed message -> EvaluationError message
        | Measured geometry ->
            match checkSingleCoverage requiredCoverage geometry with
            | Error reason -> NotEvaluated reason
            | Ok () ->
                let vertebrae = vertebralCentres snapshot
                match comparableVertebrae geometry.Center.Z vertebrae with
                | Error reason -> NotEvaluated reason
                | Ok candidates ->
                    let nearest =
                        candidates |> List.minBy (fun (_, z) -> abs (geometry.Center.Z - z))
                    let level, centerZ = nearest
                    let evidence = [NearestVertebra (level, abs (geometry.Center.Z - centerZ))]
                    if List.contains level levels then Passed evidence
                    else Flagged (Information, evidence)

    /// Applies the authored clinical priority without changing numerical observations.
    let useSeverity severity outcome =
        match outcome with
        | Flagged (_, evidence) -> Flagged (severity, evidence)
        | other -> other

    /// Evaluates one organ-local rule in the context of available scan landmarks.
    let evaluateStructureRule snapshot organ definition =
        let outcome =
            match definition.Rule with
            | Geometry rule -> evaluateIntrinsic (observation snapshot organ) rule
            | Vertebral rule -> evaluateVertebral snapshot organ rule
        { Subject = organ
          RuleId = definition.Id
          Outcome = useSeverity definition.Severity outcome }

    /// Evaluates one structure-set relation after the entire inventory has been collected.
    let evaluateStructureSetRule snapshot definition =
        let subject, outcome =
            match definition.Rule with
            | RelativePosition rule -> rule.Subject, evaluateRelative snapshot rule
            | BetweenCentres rule -> rule.Subject, evaluateBetween snapshot rule
        { Subject = subject
          RuleId = definition.Id
          Outcome = useSeverity definition.Severity outcome }

    /// Evaluates intrinsic rules first, followed by relationships across all available structures.
    let evaluate (knowledge: QaKnowledge) (snapshot: AnatomySnapshot) =
        let individual =
            knowledge.Structures
            |> List.collect (fun structure ->
                structure.Rules
                |> List.map (evaluateStructureRule snapshot structure.Organ))
        let relational =
            knowledge.StructureSetRules
            |> List.map (evaluateStructureSetRule snapshot)
        individual @ relational
