namespace StructureSetQa

open Domain

/// Exposes clinician-authored, validated rulebooks as one vendor-independent knowledge model.
module Knowledge =

    /// Builds typed clinical knowledge from four YAML rulebook contents.
    let fromYaml naming geometry relationships vertebrae : Result<QaKnowledge, string> =
        Configuration.parse naming geometry relationships vertebrae
