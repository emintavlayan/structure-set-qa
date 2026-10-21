namespace StructureSetQa.Tests

open Xunit
open StructureSetQa.Domain
open StructureSetQa

/// Provides synthetic, non-patient geometry for testing the pure QA engine.
module Fixtures =

    /// Creates a patient position in millimetres.
    let point x y z =
        { X = x; Y = y; Z = z }

    /// Creates complete grid-clearance evidence for a synthetic contour.
    let coverage distance =
        [ AnteriorEdge; PosteriorEdge; LeftEdge; RightEdge; SuperiorEdge; InferiorEdge ]
        |> List.map (fun edge -> edge, distance)
        |> MeasuredGridClearances

    /// Creates a contour observation with controlled center, component count and scan clearance.
    let measured x y z parts clearance =
        let center = point x y z
        Measured {
            ObservedVolumeCc = 100.0
            Center = center
            MeshBounds = { Minimum = center; Maximum = center }
            SeparateParts = Some parts
            Coverage = coverage clearance
            Completeness = NotEstablished
        }

    /// Creates an anatomical observation without access to clinical data.
    let structure organ contour =
        organ, { Organ = organ; ActualId = Some (string organ); Contour = contour }

    /// Creates a synthetic anatomy snapshot from one or more structure observations.
    let snapshot structures =
        let origin = point 0.0 0.0 0.0
        { StructureSetId = "Synthetic"
          Scan = { ImageId = "Synthetic"; GridBounds = { Minimum = origin; Maximum = origin }; AxisAlignedWithPatient = true }
          Structures = Map.ofList structures }

/// Tests anatomical rules independently of ESAPI and clinical records.
module AnalysisTests =

    /// Checks that the left kidney is correctly classified as left of the right kidney.
    [<Fact>]
    let ``Kidney laterality passes for expected geometry`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure (Kidney Left) (Fixtures.measured 70.0 0.0 0.0 1 20.0)
                Fixtures.structure (Kidney Right) (Fixtures.measured -70.0 0.0 0.0 1 20.0)
            ]
        let rule = {
            Other = Kidney Right
            Direction = LeftOf
            Measure = StructureCenterPoint
            MinimumDifferenceMm = 10.0
            Coverage = AnyObservedExtent
        }
        match Analysis.evaluateRelative snapshot (Kidney Left) rule with
        | Passed _ -> ()
        | other -> failwithf "Expected a pass, got %A" other

    /// Checks that a disconnected rectum receives a topology warning.
    [<Fact>]
    let ``Three rectum components require review`` () =
        let observation = Fixtures.measured 0.0 0.0 0.0 3 20.0
        match Analysis.evaluateIntrinsic observation (MaximumSeparateParts 1) with
        | Flagged (_, [ActualComponentCount 3]) -> ()
        | other -> failwithf "Expected three-part warning, got %A" other

    /// Checks that a truncated contour suppresses a whole-organ center comparison.
    [<Fact>]
    let ``CT boundary prevents coverage-dependent center comparison`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure (Kidney Left) (Fixtures.measured 60.0 0.0 60.0 1 0.5)
                Fixtures.structure Rectum (Fixtures.measured 0.0 10.0 -120.0 1 20.0)
            ]
        let rule = {
            Other = Rectum
            Direction = SuperiorTo
            Measure = StructureCenterPoint
            MinimumDifferenceMm = 20.0
            Coverage = BothClearOfGridEdges 3.0
        }
        match Analysis.evaluateRelative snapshot (Kidney Left) rule with
        | NotEvaluated _ -> ()
        | other -> failwithf "Expected no evaluation, got %A" other

    /// Checks that positive source Y is posterior, without any coordinate transformation.
    [<Fact>]
    let ``Rectum posterior to bladder uses positive Y`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure Rectum (Fixtures.measured 0.0 55.0 -100.0 1 20.0)
                Fixtures.structure Bladder (Fixtures.measured 0.0 -20.0 -90.0 1 20.0)
            ]
        let rule = {
            Other = Bladder
            Direction = PosteriorTo
            Measure = StructureCenterPoint
            MinimumDifferenceMm = 5.0
            Coverage = AnyObservedExtent
        }
        match Analysis.evaluateRelative snapshot Rectum rule with
        | Passed [ObservedDifferenceMm delta] when delta = 75.0 -> ()
        | other -> failwithf "Expected posterior relation to pass, got %A" other

    /// Checks that the right kidney cannot satisfy a left-of rule.
    [<Fact>]
    let ``Right-sided structure fails left-of comparison`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure (Kidney Left) (Fixtures.measured -50.0 0.0 0.0 1 20.0)
                Fixtures.structure (Kidney Right) (Fixtures.measured 50.0 0.0 0.0 1 20.0)
            ]
        let rule = {
            Other = Kidney Right
            Direction = LeftOf
            Measure = StructureCenterPoint
            MinimumDifferenceMm = 10.0
            Coverage = AnyObservedExtent
        }
        match Analysis.evaluateRelative snapshot (Kidney Left) rule with
        | Flagged (_, [ObservedDifferenceMm delta]) when delta = -100.0 -> ()
        | other -> failwithf "Expected laterality warning, got %A" other
