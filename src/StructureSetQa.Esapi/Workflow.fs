namespace StructureSetQa

open FsToolkit.ErrorHandling
open VMS.TPS.Common.Model.API
open Domain

/// Orchestrates an initial read-only structure-set review from an ESAPI context.
module Workflow =

    /// Runs initial structure-set QA without creating or modifying any ESAPI objects.
    let runInitialReview (scriptContext: ScriptContext) : Result<QaReport, string> =
        result {
            let knowledge = Knowledge.dayOne
            let! snapshot = EsapiReader.readSnapshot scriptContext knowledge
            let findings = Analysis.evaluate knowledge snapshot

            return {
                Stage = InitialContourReview
                StructureSetId = snapshot.StructureSetId
                Findings = findings
            }
        }
