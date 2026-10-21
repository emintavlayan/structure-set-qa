namespace StructureSetQa

open FsToolkit.ErrorHandling
open VMS.TPS.Common.Model.API
open Domain

/// Orchestrates a read-only, pre-planning contour QA run from an Eclipse context.
module Workflow =

    /// Runs the first-stage contour QA without creating or modifying any ESAPI objects.
    let runBeforePlanning (scriptContext: ScriptContext) : Result<QaReport, string> =
        result {
            let knowledge = Knowledge.dayOne
            let! snapshot = EsapiReader.readSnapshot scriptContext knowledge
            let findings = Analysis.evaluate knowledge snapshot

            return {
                Stage = BeforePlanning
                StructureSetId = snapshot.StructureSetId
                Findings = findings
            }
        }
