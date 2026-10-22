namespace StructureSetQa.Cli

open StructureSetQa
open StructureSetQa.Domain

/// Provides synthetic anatomy for local development without clinical systems or patient data.
module Demo =

    /// Represents a synthetic source structure used in the demonstration.
    type NamedContour = {
        Id: string
        Center: PatientPoint
        Parts: int
    }

    /// Creates one synthetic contour observation from source XYZ millimetres.
    let contour id x y z parts =
        { Id = id
          Center = { X = x; Y = y; Z = z }
          Parts = parts }

    /// Creates consecutive labeled vertebral center observations.
    let vertebrae () =
        [ for level in 3 .. 12 ->
              contour (sprintf "T%d" level) 0.0 65.0 (350.0 - float (level - 3) * 30.0) 1
          for level in 1 .. 5 ->
              contour (sprintf "L%d" level) 0.0 65.0 (50.0 - float (level - 1) * 30.0) 1
          for level in 1 .. 5 ->
              contour (sprintf "S%d" level) 0.0 65.0 (-100.0 - float (level - 1) * 30.0) 1 ]

    /// Creates representative synthetic organs, deliberately giving the rectum three components.
    let organs () =
        [ contour "Rectum" 0.0 30.0 -210.0 3
          contour "Bladder" 0.0 -25.0 -190.0 1
          contour "Sacrum" 0.0 65.0 -225.0 1
          contour "Kidney_L" 75.0 30.0 100.0 1
          contour "Kidney_R" -75.0 30.0 90.0 1
          contour "Lung_L" 90.0 -20.0 320.0 1
          contour "Lung_R" -90.0 -20.0 320.0 1
          contour "Heart" 20.0 -10.0 270.0 1
          contour "Liver" -120.0 -40.0 130.0 1
          contour "Spleen" 110.0 10.0 140.0 1 ]

    /// Assigns available contour geometry and synthetic scan clearances.
    let geometry (contour: NamedContour) =
        let distances =
            [ AnteriorEdge; PosteriorEdge; LeftEdge; RightEdge; SuperiorEdge; InferiorEdge ]
            |> List.map (fun edge -> edge, 20.0)

        Measured {
            ObservedVolumeCc = 100.0
            Center = contour.Center
            MeshBounds = {
                Minimum = contour.Center
                Maximum = contour.Center
            }
            SeparateParts = Some contour.Parts
            Coverage = MeasuredGridClearances distances
            Completeness = NotEstablished
        }

    /// Builds a recognized synthetic structure set using the same inventory resolver as the adapter.
    let snapshot (knowledge: QaKnowledge) : AnatomySnapshot =
        let contours = organs () @ vertebrae ()
        let inventory =
            contours
            |> List.map (fun item -> item.Id)
            |> StructureNames.inventory knowledge.Naming

        let byId = contours |> List.map (fun item -> item.Id, item) |> Map.ofList
        let observations =
            inventory.Items
            |> List.choose (fun item ->
                match item.Match, Map.tryFind item.ActualId byId with
                | Recognized organ, Some source ->
                    Some (organ, {
                        Organ = organ
                        ActualId = Some source.Id
                        Contour = geometry source
                    })
                | _ -> None)
            |> Map.ofList

        let origin = { X = 0.0; Y = 0.0; Z = 0.0 }
        { StructureSetId = "SYNTHETIC-STRUCTURE-SET"
          Scan = {
              ImageId = "SYNTHETIC-CT"
              GridBounds = { Minimum = origin; Maximum = origin }
              AxisAlignedWithPatient = true
          }
          Inventory = inventory
          Structures = observations }

    /// Evaluates the synthetic structure set using the actual loaded clinical YAML rules.
    let run knowledge =
        let sample = snapshot knowledge
        { Stage = InitialContourReview
          StructureSetId = sample.StructureSetId
          Inventory = sample.Inventory
          Findings = Analysis.evaluate knowledge sample }
