namespace StructureSetQa

open Domain

/// Evaluates clinical rules using only immutable structure-set observations.
module Analysis =

    /// Gets an observation, representing an unmatched structure as missing.
    let observation (snapshot: AnatomySnapshot) organ =
        snapshot.Structures
        |> Map.tryFind organ
        |> Option.map (fun value -> value.Contour)
        |> Option.defaultValue Missing

    /// Gets the minimum known distance from a contour to the CT grid boundary.
    let minimumGridClearance (geometry: ContourGeometry) =
        match geometry.Coverage with
        | NotAssessed reason -> Error reason
        | MeasuredGridClearances [] -> Error "CT grid clearances were not measured."
        | MeasuredGridClearances values -> values |> List.map snd |> List.min |> Ok

    /// Gets the anatomical point selected by a position rule.
    let measuredLocation measure (geometry: ContourGeometry) =
        match measure with
        | StructureCenterPoint -> geometry.Center
        | BoundingBoxCentre ->
            { X = (geometry.MeshBounds.Minimum.X + geometry.MeshBounds.Maximum.X) / 2.0
              Y = (geometry.MeshBounds.Minimum.Y + geometry.MeshBounds.Maximum.Y) / 2.0
              Z = (geometry.MeshBounds.Minimum.Z + geometry.MeshBounds.Maximum.Z) / 2.0 }
        | SuperiorExtent ->
            { geometry.Center with Z = geometry.MeshBounds.Maximum.Z }
        | InferiorExtent ->
            { geometry.Center with Z = geometry.MeshBounds.Minimum.Z }

    /// Calculates signed anatomical separation, positive when the rule's direction holds.
    let signedDifference direction first second =
        match direction with
        | AnteriorTo -> second.Y - first.Y
        | PosteriorTo -> first.Y - second.Y
        | LeftOf -> first.X - second.X
        | RightOf -> second.X - first.X
        | SuperiorTo -> first.Z - second.Z
        | InferiorTo -> second.Z - first.Z

    /// Checks whether the supplied scan coverage is adequate for a center comparison.
    let hasRequiredCoverage requirement first second =
        match requirement with
        | AnyObservedExtent -> Ok ()
        | BothClearOfGridEdges marginMm ->
            match minimumGridClearance first, minimumGridClearance second with
            | Ok left, Ok right when left > marginMm && right > marginMm -> Ok ()
            | Ok _, Ok _ -> Error "One or both contours approach the acquired CT grid boundary."
            | Error reason, _ -> Error reason
            | _, Error reason -> Error reason

    /// Evaluates a rule about one contour without interpreting partial anatomy as an error.
    let evaluateIntrinsic observation rule =
        match rule, observation with
        | MustBePresent, Missing -> Flagged (Warning, [])
        | MustBePresent, Empty -> Flagged (Warning, [])
        | MustBePresent, Measured geometry -> Passed [ObservedStructureVolumeCc geometry.ObservedVolumeCc]
        | _, Missing -> NotEvaluated "Structure is not present in the current structure set."
        | _, Empty -> NotEvaluated "Structure exists but its contour is empty."
        | _, ExtractionFailed message -> EvaluationError message
        | MaximumSeparateParts maximum, Measured geometry ->
            match geometry.SeparateParts with
            | None -> NotEvaluated "Separate-part count is unavailable from this ESAPI version."
            | Some count when count < 1 -> EvaluationError "ESAPI returned an invalid component count."
            | Some count when count <= maximum -> Passed [ActualComponentCount count]
            | Some count -> Flagged (Warning, [ActualComponentCount count])
        | ReportGridEdgeProximity marginMm, Measured geometry ->
            match geometry.Coverage with
            | NotAssessed reason -> NotEvaluated reason
            | MeasuredGridClearances [] -> NotEvaluated "CT grid clearances are unavailable."
            | MeasuredGridClearances distances ->
                let nearby = distances |> List.filter (fun (_, distance) -> distance <= marginMm)
                if List.isEmpty nearby then
                    let clearance = distances |> List.map snd |> List.min
                    Passed [ScanClearanceMm clearance]
                else
                    Flagged (Information, [BoundaryEdges (nearby |> List.map fst)])

    /// Evaluates relative position only when both observed contours are usable.
    let evaluateRelative snapshot owner rule =
        match observation snapshot owner, observation snapshot rule.Other with
        | Measured first, Measured second ->
            match hasRequiredCoverage rule.Coverage first second with
            | Error reason -> NotEvaluated reason
            | Ok () ->
                let firstPoint = measuredLocation rule.Measure first
                let secondPoint = measuredLocation rule.Measure second
                let delta = signedDifference rule.Direction firstPoint secondPoint
                if delta >= rule.MinimumDifferenceMm then
                    Passed [ObservedDifferenceMm delta]
                else
                    Flagged (Warning, [ObservedDifferenceMm delta])
        | ExtractionFailed reason, _ -> EvaluationError reason
        | _, ExtractionFailed reason -> EvaluationError reason
        | _ -> NotEvaluated "One or both required structures are absent or empty."

    /// Evaluates a single named rule and preserves its configured severity.
    let evaluateRule snapshot owner (definition: RuleDefinition) =
        let outcome =
            match definition.Rule with
            | Intrinsic rule -> evaluateIntrinsic (observation snapshot owner) rule
            | Relative rule -> evaluateRelative snapshot owner rule
            | Vertebral _ -> NotEvaluated "Labeled vertebral landmarks are not available in day one."

        let withConfiguredSeverity =
            match outcome with
            | Flagged (_, evidence) -> Flagged (definition.Severity, evidence)
            | other -> other

        { Organ = owner; RuleId = definition.Id; Outcome = withConfiguredSeverity }

    /// Evaluates all currently configured structures and their nested rules.
    let evaluate (knowledge: QaKnowledge) (snapshot: AnatomySnapshot) =
        knowledge.Structures
        |> List.collect (fun structure ->
            structure.Rules |> List.map (evaluateRule snapshot structure.Organ))
