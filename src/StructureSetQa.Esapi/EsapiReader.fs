namespace StructureSetQa

open System
open System.Windows.Media.Media3D
open FsToolkit.ErrorHandling
open VMS.TPS.Common.Model.API
open VMS.TPS.Common.Model.Types
open Domain

/// Reads immutable contour and CT observations from a live Eclipse ScriptContext.
module EsapiReader =

    /// Converts DICOM LPS coordinates into anterior-positive, left-positive patient coordinates.
    let fromLps x y z =
        { AnteriorMm = -y; LeftMm = x; SuperiorMm = z }

    /// Converts the ESAPI center point into patient coordinates.
    let fromVVector (vector: VVector) = fromLps vector.x vector.y vector.z

    /// Converts a mesh's axis-aligned DICOM bounding box into patient coordinates.
    let fromMeshBounds (box: Rect3D) =
        { Minimum = fromLps box.X (box.Y + box.SizeY) box.Z
          Maximum = fromLps (box.X + box.SizeX) box.Y (box.Z + box.SizeZ) }

    /// Creates the physical point at a voxel-corner index, using image direction vectors.
    let imagePoint (image: Image) x y z =
        let xMm = x * image.XRes
        let yMm = y * image.YRes
        let zMm = z * image.ZRes
        fromLps
            (image.Origin.x + xMm * image.XDirection.x + yMm * image.YDirection.x + zMm * image.ZDirection.x)
            (image.Origin.y + xMm * image.XDirection.y + yMm * image.YDirection.y + zMm * image.ZDirection.y)
            (image.Origin.z + xMm * image.XDirection.z + yMm * image.YDirection.z + zMm * image.ZDirection.z)

    /// Computes a patient-axis-aligned envelope around all eight CT voxel-grid corners.
    let imageBounds (image: Image) =
        let corners =
            [ for x in [ -0.5; float image.XSize - 0.5 ] do
                for y in [ -0.5; float image.YSize - 0.5 ] do
                    for z in [ -0.5; float image.ZSize - 0.5 ] do
                        imagePoint image x y z ]

        { Minimum =
            { AnteriorMm = corners |> List.minBy (fun p -> p.AnteriorMm) |> fun p -> p.AnteriorMm
              LeftMm = corners |> List.minBy (fun p -> p.LeftMm) |> fun p -> p.LeftMm
              SuperiorMm = corners |> List.minBy (fun p -> p.SuperiorMm) |> fun p -> p.SuperiorMm }
          Maximum =
            { AnteriorMm = corners |> List.maxBy (fun p -> p.AnteriorMm) |> fun p -> p.AnteriorMm
              LeftMm = corners |> List.maxBy (fun p -> p.LeftMm) |> fun p -> p.LeftMm
              SuperiorMm = corners |> List.maxBy (fun p -> p.SuperiorMm) |> fun p -> p.SuperiorMm } }

    /// Checks whether the acquisition-grid directions follow patient cardinal axes.
    let isCardinalGrid (image: Image) =
        let isZero value = abs value < 0.001
        let isUnit value = abs (abs value - 1.0) < 0.001
        let x = image.XDirection
        let y = image.YDirection
        let z = image.ZDirection
        let inPlane direction =
            isZero direction.z &&
            ((isUnit direction.x && isZero direction.y) ||
             (isUnit direction.y && isZero direction.x))
        inPlane x && inPlane y && isUnit z.z && isZero z.x && isZero z.y

    /// Measures signed distances between the observed contour bounds and all six grid faces.
    let gridClearances scanBounds structureBounds =
        [ AnteriorEdge, scanBounds.Maximum.AnteriorMm - structureBounds.Maximum.AnteriorMm
          PosteriorEdge, structureBounds.Minimum.AnteriorMm - scanBounds.Minimum.AnteriorMm
          LeftEdge, scanBounds.Maximum.LeftMm - structureBounds.Maximum.LeftMm
          RightEdge, structureBounds.Minimum.LeftMm - scanBounds.Minimum.LeftMm
          SuperiorEdge, scanBounds.Maximum.SuperiorMm - structureBounds.Maximum.SuperiorMm
          InferiorEdge, structureBounds.Minimum.SuperiorMm - scanBounds.Minimum.SuperiorMm ]

    /// Reads a number-of-separate-parts method if supplied by the installed ESAPI assembly.
    let trySeparateParts (structure: Structure) =
        let methodInfo = typeof<Structure>.GetMethod("GetNumberOfSeparateParts", Type.EmptyTypes)
        if isNull methodInfo then None
        else Some (methodInfo.Invoke(structure, [||]) :?> int)

    /// Extracts one nonempty contour using existing ESAPI measurements and mesh bounds.
    let readGeometry (scan: ScanObservation) (structure: Structure) =
        let mesh = structure.MeshGeometry
        if isNull mesh then
            Error "ESAPI did not provide mesh geometry for this nonempty structure."
        else
            let bounds = fromMeshBounds mesh.Bounds
            let coverage =
                if scan.AxisAlignedWithPatient then
                    gridClearances scan.GridBounds bounds |> MeasuredGridClearances
                else
                    NotAssessed "The CT grid is oblique; axis-aligned envelope clearance is not reliable."
            Ok {
                ObservedVolumeCc = structure.Volume
                EsapiCenter = fromVVector structure.CenterPoint
                MeshBounds = bounds
                SeparateParts = trySeparateParts structure
                Coverage = coverage
                Completeness = NotEstablished
            }

    /// Finds a structure using the current Siemens ID list, without fuzzy matching.
    let findStructure (structureSet: StructureSet) ids =
        structureSet.Structures
        |> Seq.tryFind (fun structure ->
            ids |> List.exists (fun id -> String.Equals(structure.Id, id, StringComparison.OrdinalIgnoreCase)))

    /// Reads one expected organ while isolating failures to that organ.
    let readOrgan scan (structureSet: StructureSet) (definition: StructureDefinition) =
        match findStructure structureSet definition.SourceIds with
        | None -> { Organ = definition.Organ; ActualId = None; Contour = Missing }
        | Some structure ->
            let contour =
                try
                    if structure.IsEmpty then Empty
                    else
                        match readGeometry scan structure with
                        | Ok geometry -> Measured geometry
                        | Error message -> ExtractionFailed message
                with exceptionInfo -> ExtractionFailed exceptionInfo.Message
            { Organ = definition.Organ
              ActualId = Some structure.Id
              Contour = contour }

    /// Validates and reads the structure set selected in the live ScriptContext.
    let readSnapshot (scriptContext: ScriptContext) (knowledge: QaKnowledge) : Result<AnatomySnapshot, string> =
        result {
            let! structureSet =
                if isNull scriptContext then Error "No active Eclipse ScriptContext."
                elif isNull scriptContext.StructureSet then Error "No active structure set."
                else Ok scriptContext.StructureSet

            let! image =
                if isNull structureSet.Image then Error "The structure set has no associated image."
                else Ok structureSet.Image

            let! _ =
                if image.XSize > 0 && image.YSize > 0 && image.ZSize > 0 then Ok ()
                else Error "The image has invalid 3D grid dimensions."

            let scan = {
                ImageId = image.Id
                GridBounds = imageBounds image
                AxisAlignedWithPatient = isCardinalGrid image
            }

            let observations =
                knowledge.Structures
                |> List.map (fun definition ->
                    definition.Organ, readOrgan scan structureSet definition)
                |> Map.ofList

            return {
                StructureSetId = structureSet.UID
                Scan = scan
                Structures = observations
            }
        }
