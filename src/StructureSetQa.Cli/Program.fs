namespace StructureSetQa.Cli

open System
open System.IO
open FsToolkit.ErrorHandling
open StructureSetQa
open StructureSetQa.Domain

/// Runs local, vendor-independent rulebook validation and synthetic QA demonstrations.
module Program =

    /// Reads one text file as a fallible operation at the CLI boundary.
    let readText path : Result<string, string> =
        try
            if File.Exists path then Ok (File.ReadAllText path)
            else Error (sprintf "File does not exist: %s" path)
        with error -> Error (sprintf "Cannot read '%s': %s" path error.Message)

    /// Loads the four human-editable rulebooks into the typed clinical domain.
    let loadKnowledge directory : Result<QaKnowledge, string> =
        result {
            let! names = readText (Path.Combine(directory, "structure-ids.yaml"))
            let! geometry = readText (Path.Combine(directory, "geometry.yaml"))
            let! relationships = readText (Path.Combine(directory, "relationships.yaml"))
            let! vertebrae = readText (Path.Combine(directory, "vertebrae.yaml"))
            return! Knowledge.fromYaml names geometry relationships vertebrae
        }

    /// Displays a level with its conventional short vertebral label.
    let levelName =
        function
        | Cervical number -> sprintf "C%d" number
        | Thoracic number -> sprintf "T%d" number
        | Lumbar number -> sprintf "L%d" number
        | Sacral number -> sprintf "S%d" number

    /// Displays the name of an anatomical structure in a finding or inventory row.
    let organName =
        function
        | Vertebra level -> levelName level
        | organ -> OrganIds.format organ

    /// Displays the exact matching outcome for a source structure name.
    let nameStatus =
        function
        | Recognized organ -> sprintf "recognized  %-20s" (organName organ)
        | Unrecognized -> "unrecognized"
        | ExcludedModifier modifier -> sprintf "excluded    %s" modifier
        | Ambiguous organs ->
            organs
            |> List.map organName
            |> String.concat ", "
            |> sprintf "ambiguous   %s"

    /// Displays a structure-set inventory without exposing any clinical geometry.
    let printInventory (inventory: StructureInventory) =
        printfn "Structure inventory: %d name(s)" inventory.Items.Length
        for item in inventory.Items do
            printfn "  %-28s  %s" item.ActualId (nameStatus item.Match)

        for (organ, ids) in inventory.DuplicateOrgans do
            printfn "  DUPLICATE %-20s  %s" (organName organ) (String.concat ", " ids)

    /// Formats typed QA evidence for readable local inspection.
    let describeEvidence =
        function
        | ActualComponentCount count -> sprintf "components=%d" count
        | ObservedDifferenceMm value -> sprintf "signed_difference=%.1f mm" value
        | ScanClearanceMm value -> sprintf "scan_clearance=%.1f mm" value
        | BoundaryEdges edges -> sprintf "scan_faces=%A" edges
        | ObservedStructureVolumeCc value -> sprintf "volume=%.1f cm3" value
        | NearestVertebra (level, distance) ->
            sprintf "nearest=%s, delta_z=%.1f mm" (levelName level) distance

    /// Converts a rule evaluation into a descriptive status and evidence.
    let outcomeText =
        function
        | Passed evidence -> "PASS", evidence |> List.map describeEvidence
        | Flagged (severity, evidence) ->
            sprintf "REVIEW (%A)" severity, evidence |> List.map describeEvidence
        | NotEvaluated reason -> "NOT EVALUATED", [reason]
        | EvaluationError message -> "ERROR", [message]

    /// Displays all rule findings and a compact execution summary.
    let printReport (report: QaReport) =
        printfn "Structure-set QA: %s" report.StructureSetId
        printfn "Stage: %A" report.Stage
        printInventory report.Inventory
        printfn ""
        for finding in report.Findings do
            let status, evidence = outcomeText finding.Outcome
            printfn "  %-18s  %-34s  %s" status (organName finding.Subject) finding.RuleId
            for item in evidence do
                printfn "      %s" item
        printfn ""
        let count predicate =
            report.Findings |> List.filter (fun item -> predicate item.Outcome) |> List.length

        printfn "Summary: %d passed, %d review, %d not evaluated, %d errors"
            (count (function Passed _ -> true | _ -> false))
            (count (function Flagged _ -> true | _ -> false))
            (count (function NotEvaluated _ -> true | _ -> false))
            (count (function EvaluationError _ -> true | _ -> false))

    /// Parses a simple text file with one structure ID on each noncomment line.
    let readStructureIds path : Result<string list, string> =
        readText path
        |> Result.bind (fun value ->
            let names =
                value.Replace("\r", "").Split('\n')
                |> Array.map (fun line -> line.Trim())
                |> Array.filter (fun line -> line <> "" && not (line.StartsWith("#")))
                |> Array.toList
            if List.isEmpty names then Error "The structure-ID file is empty."
            else Ok names)

    /// Validates the rulebooks without connecting to a treatment planning system.
    let validate rulesDirectory : Result<unit, string> =
        result {
            let! knowledge = loadKnowledge rulesDirectory
            let geometryCount = knowledge.Structures |> List.sumBy (fun item -> item.Rules.Length)
            printfn "Valid rulebooks: version %d" knowledge.Version
            printfn "Organ identities: %d" knowledge.Structures.Length
            printfn "Structure-level rules: %d" geometryCount
            printfn "Structure-set relations: %d" knowledge.StructureSetRules.Length
        }

    /// Tests the structure-ID dictionary against a local, names-only text file.
    let inventory rulesDirectory idsFile : Result<unit, string> =
        result {
            let! knowledge = loadKnowledge rulesDirectory
            let! ids = readStructureIds idsFile
            ids
            |> StructureNames.inventory knowledge.Naming
            |> printInventory
        }

    /// Runs a deterministic synthetic QA case to exercise the real rulebooks.
    let demo rulesDirectory : Result<unit, string> =
        result {
            let! knowledge = loadKnowledge rulesDirectory
            printfn "Synthetic example only — no patient or clinical data.\n"
            Demo.run knowledge |> printReport
        }

    /// Shows the supported commands and optional local rulebook directory.
    let help () =
        printfn "structure-set-qa — local development CLI"
        printfn ""
        printfn "Usage:"
        printfn "  validate [rules-directory]"
        printfn "  inventory <structure-ids-file> [rules-directory]"
        printfn "  demo [rules-directory]"
        printfn ""
        printfn "Examples (from repository root):"
        printfn "  dotnet run --project src/StructureSetQa.Cli -- validate"
        printfn "  dotnet run --project src/StructureSetQa.Cli -- inventory samples/structure-ids.txt"
        printfn "  dotnet run --project src/StructureSetQa.Cli -- demo"

    /// Routes CLI arguments to one explicit, testable action.
    let run (args: string array) : Result<unit, string> =
        match Array.toList args with
        | [] | ["help"] | ["--help"] -> help (); Ok ()
        | ["validate"] -> validate "rules"
        | ["validate"; directory] -> validate directory
        | ["inventory"; file] -> inventory "rules" file
        | ["inventory"; file; directory] -> inventory directory file
        | ["demo"] -> demo "rules"
        | ["demo"; directory] -> demo directory
        | _ -> Error "Unknown arguments. Run with --help to see supported commands."

    /// Returns a process exit code that reflects configuration or input failure.
    [<EntryPoint>]
    let main args =
        match run args with
        | Ok () -> 0
        | Error message ->
            eprintfn "Error: %s" message
            1
