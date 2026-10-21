namespace StructureSetQa.Tests

open System
open System.IO
open Xunit
open StructureSetQa.Domain
open StructureSetQa

/// Provides synthetic, nonpatient observations for rule and resolver tests.
module Fixtures =

    /// Creates a source-coordinate point in millimetres.
    let point x y z = { X = x; Y = y; Z = z }

    /// Creates comparable, complete CT-grid clearances.
    let coverage distance =
        [ AnteriorEdge; PosteriorEdge; LeftEdge; RightEdge; SuperiorEdge; InferiorEdge ]
        |> List.map (fun edge -> edge, distance)
        |> MeasuredGridClearances

    /// Creates a measured contour without patient data.
    let measured x y z parts clearance =
        let point = point x y z
        Measured {
            ObservedVolumeCc = 100.0
            Center = point
            MeshBounds = { Minimum = point; Maximum = point }
            SeparateParts = Some parts
            Coverage = coverage clearance
            Completeness = NotEstablished
        }

    /// Creates one named synthetic structure observation.
    let structure organ contour =
        organ, { Organ = organ; ActualId = Some (string organ); Contour = contour }

    /// Creates an inventory-free geometry fixture from named structures.
    let snapshot structures =
        let origin = point 0.0 0.0 0.0
        { StructureSetId = "Synthetic"
          Scan = {
            ImageId = "Synthetic"
            GridBounds = { Minimum = origin; Maximum = origin }
            AxisAlignedWithPatient = true
          }
          Inventory = { Items = []; DuplicateOrgans = [] }
          Structures = Map.ofList structures }

    /// Reads a checked-in clinical example rulebook copied into the test output.
    let ruleFile name = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules", name))

    /// Loads all four real rulebooks through the production parser.
    let knowledge () =
        Knowledge.fromYaml
            (ruleFile "structure-ids.yaml")
            (ruleFile "geometry.yaml")
            (ruleFile "relationships.yaml")
            (ruleFile "vertebrae.yaml")

/// Verifies name resolution, knowledge compilation, and anatomical rule evaluation.
module AnalysisTests =

    /// Ensures the checked-in clinician-facing YAML converts into domain rule types.
    [<Fact>]
    let ``Example clinical rulebooks parse successfully`` () =
        match Fixtures.knowledge () with
        | Ok knowledge ->
            Assert.NotEmpty knowledge.Structures
            Assert.NotEmpty knowledge.StructureSetRules
        | Error message -> failwith message

    /// Resolves Danish names and standard laterality variants without fuzzy matching.
    [<Theory>]
    [<InlineData("Rektum", "rectum")>]
    [<InlineData("Kidney_L", "kidney_left")>]
    [<InlineData("Kidney-Rt", "kidney_right")>]
    [<InlineData("LEFT KIDNEY", "kidney_left")>]
    [<InlineData("Nyre H", "kidney_right")>]
    [<InlineData("Femoral Head Left", "femoral_head_left")>]
    let ``Aliases resolve to expected organ identities`` actualId expected =
        match Fixtures.knowledge () with
        | Error message -> failwith message
        | Ok knowledge ->
            match StructureNames.resolve knowledge.Naming actualId with
            | Recognized organ -> Assert.Equal(expected, OrganIds.format organ)
            | result -> failwithf "Unexpected resolution: %A" result

    /// Excludes modified PRV structures from ordinary anatomical organ identity.
    [<Fact>]
    let ``PRV is not treated as the organ itself`` () =
        match Fixtures.knowledge () with
        | Error message -> failwith message
        | Ok knowledge ->
            match StructureNames.resolve knowledge.Naming "Kidney_L_PRV" with
            | ExcludedModifier "PRV" -> ()
            | other -> failwithf "Expected PRV exclusion, got %A" other

    /// Identifies a labeled vertebra independently of organ aliases.
    [<Fact>]
    let ``Thoracic vertebral names resolve`` () =
        match Fixtures.knowledge () with
        | Error message -> failwith message
        | Ok knowledge ->
            match StructureNames.resolve knowledge.Naming "Vertebra_T09" with
            | Recognized (Vertebra (Thoracic 9)) -> ()
            | other -> failwithf "Expected T9, got %A" other

    /// Preserves unrecognized names in the inventory instead of fabricating an organ.
    [<Fact>]
    let ``Inventory retains unknown and duplicate structure IDs`` () =
        match Fixtures.knowledge () with
        | Error message -> failwith message
        | Ok knowledge ->
            let inventory =
                StructureNames.inventory knowledge.Naming
                    ["Rectum"; "Rektum"; "Kidney_L"; "Tumour_AI_1"]
            Assert.Single inventory.DuplicateOrgans |> ignore
            Assert.Contains(inventory.Items, fun item -> item.Match = Unrecognized)

    /// Detects an abnormal connected-component count for a rectum.
    [<Fact>]
    let ``Three rectum parts trigger a warning`` () =
        let contour = Fixtures.measured 0.0 0.0 0.0 3 20.0
        match Analysis.evaluateIntrinsic contour (MaximumSeparateParts 1) with
        | Flagged (_, [ActualComponentCount 3]) -> ()
        | other -> failwithf "Expected topology warning, got %A" other

    /// Confirms that increasing source X corresponds to patient left.
    [<Fact>]
    let ``Left kidney is left of right kidney in source X`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure (Kidney Left) (Fixtures.measured 75.0 0.0 0.0 1 20.0)
                Fixtures.structure (Kidney Right) (Fixtures.measured -75.0 0.0 0.0 1 20.0)
            ]
        let rule = {
            Subject = Kidney Left
            Reference = Kidney Right
            Direction = LeftOf
            Measure = StructureCenterPoint
            MinimumDifferenceMm = 10.0
            Coverage = AnyObservedExtent
        }
        match Analysis.evaluateRelative snapshot rule with
        | Passed [ObservedDifferenceMm 150.0] -> ()
        | other -> failwithf "Expected left-of pass, got %A" other

    /// Confirms that increasing source Y corresponds to patient posterior.
    [<Fact>]
    let ``Rectum is posterior to bladder in source Y`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure Rectum (Fixtures.measured 0.0 50.0 -100.0 1 20.0)
                Fixtures.structure Bladder (Fixtures.measured 0.0 -20.0 -90.0 1 20.0)
            ]
        let rule = {
            Subject = Rectum
            Reference = Bladder
            Direction = PosteriorTo
            Measure = StructureCenterPoint
            MinimumDifferenceMm = 5.0
            Coverage = AnyObservedExtent
        }
        match Analysis.evaluateRelative snapshot rule with
        | Passed [ObservedDifferenceMm 70.0] -> ()
        | other -> failwithf "Expected posterior pass, got %A" other

    /// Confirms that the heart center can fall between the two lung centers.
    [<Fact>]
    let ``Heart lies between lung centers`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure Heart (Fixtures.measured 20.0 0.0 0.0 1 20.0)
                Fixtures.structure (Lung Left) (Fixtures.measured 85.0 0.0 0.0 1 20.0)
                Fixtures.structure (Lung Right) (Fixtures.measured -85.0 0.0 0.0 1 20.0)
            ]
        let rule = {
            Subject = Heart
            First = Lung Left
            Second = Lung Right
            Axis = 'X'
            MarginMm = 0.0
            Coverage = AnyObservedExtent
        }
        match Analysis.evaluateBetween snapshot rule with
        | Passed _ -> ()
        | other -> failwithf "Expected heart between lungs, got %A" other

    /// Avoids comparing partial organs to whole-organ position assumptions.
    [<Fact>]
    let ``CT edge disables a coverage-dependent relation`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure (Kidney Left) (Fixtures.measured 60.0 0.0 60.0 1 0.5)
                Fixtures.structure Rectum (Fixtures.measured 0.0 10.0 -120.0 1 20.0)
            ]
        let rule = {
            Subject = Kidney Left
            Reference = Rectum
            Direction = SuperiorTo
            Measure = StructureCenterPoint
            MinimumDifferenceMm = 20.0
            Coverage = BothClearOfGridEdges 3.0
        }
        match Analysis.evaluateRelative snapshot rule with
        | NotEvaluated _ -> ()
        | other -> failwithf "Expected suppression for partial CT, got %A" other

    /// Selects the nearest labeled vertebra by superior-inferior centroid distance.
    [<Fact>]
    let ``Kidney center is nearest to T12`` () =
        let snapshot =
            Fixtures.snapshot [
                Fixtures.structure (Kidney Left) (Fixtures.measured 80.0 0.0 5.0 1 20.0)
                Fixtures.structure (Vertebra (Thoracic 12)) (Fixtures.measured 0.0 50.0 0.0 1 20.0)
                Fixtures.structure (Vertebra (Lumbar 1)) (Fixtures.measured 0.0 50.0 -30.0 1 20.0)
                Fixtures.structure (Vertebra (Thoracic 11)) (Fixtures.measured 0.0 50.0 30.0 1 20.0)
            ]
        match Analysis.evaluateVertebral snapshot (Kidney Left)
            (NearestVertebraAllowed ([Thoracic 12; Lumbar 1], AnyObservedExtent)) with
        | Passed [NearestVertebra (Thoracic 12, 5.0)] -> ()
        | other -> failwithf "Expected T12, got %A" other

    /// Rejects a rule that uses an unknown anatomical direction.
    [<Fact>]
    let ``Malformed rulebook is rejected with a clear error`` () =
        let wrong =
            (Fixtures.ruleFile "relationships.yaml")
                .Replace("left_of", "diagonal_to")
        let result =
            Knowledge.fromYaml
                (Fixtures.ruleFile "structure-ids.yaml")
                (Fixtures.ruleFile "geometry.yaml")
                wrong
                (Fixtures.ruleFile "vertebrae.yaml")
        match result with
        | Error message -> Assert.Contains("unsupported direction", message)
        | Ok _ -> failwith "Malformed direction was accepted."
