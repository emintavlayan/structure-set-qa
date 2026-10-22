namespace StructureSetQa

open System
open System.IO
open FsToolkit.ErrorHandling
open VMS.TPS.Common.Model.API
open Domain

/// Orchestrates read-only structure-set QA using clinician-maintained local rulebooks.
module Workflow =

    /// Reads one named configuration file from the explicitly provided rules directory.
    let readRulebook directory filename : Result<string, string> =
        try
            let path = Path.Combine(directory, filename)
            if File.Exists path then Ok (File.ReadAllText path)
            else Error ("Missing clinical rulebook: " + path)
        with error -> Error (sprintf "Unable to read %s: %s" filename error.Message)

    /// Loads and validates all four clinician-authored rulebooks before ESAPI extraction.
    let loadKnowledge directory : Result<QaKnowledge, string> =
        result {
            let! naming = readRulebook directory "structure-ids.yaml"
            let! geometry = readRulebook directory "geometry.yaml"
            let! relationships = readRulebook directory "relationships.yaml"
            let! vertebrae = readRulebook directory "vertebrae.yaml"
            return! Knowledge.fromYaml naming geometry relationships vertebrae
        }

    /// Runs initial QA and returns both the report and immutable extracted measurements.
    let runInitialReviewDetailed directory (scriptContext: ScriptContext) : Result<QaReport * AnatomySnapshot, string> =
        result {
            let! knowledge = loadKnowledge directory
            let! snapshot = EsapiReader.readSnapshot scriptContext knowledge
            let findings = Analysis.evaluate knowledge snapshot

            let report = {
                Stage = InitialContourReview
                StructureSetId = snapshot.StructureSetId
                Inventory = snapshot.Inventory
                Findings = findings
            }
            return report, snapshot
        }

    /// Returns the initial structure-set QA report to callers that do not need measurements.
    let runInitialReview directory (scriptContext: ScriptContext) : Result<QaReport, string> =
        runInitialReviewDetailed directory scriptContext
        |> Result.map fst
