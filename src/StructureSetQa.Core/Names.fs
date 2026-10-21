namespace StructureSetQa

open System
open System.Globalization
open System.Text
open System.Text.RegularExpressions
open Domain

/// Maps human-editable organ keys to domain identities.
module OrganIds =

    /// Parses a canonical rulebook key into an anatomical identity.
    let parse key : Result<Organ, string> =
        match key.Trim().ToLowerInvariant() with
        | "rectum" -> Ok Rectum
        | "bladder" -> Ok Bladder
        | "prostate" -> Ok Prostate
        | "seminal_vesicles" -> Ok SeminalVesicles
        | "uterus" -> Ok Uterus
        | "kidney_left" -> Ok (Kidney Left)
        | "kidney_right" -> Ok (Kidney Right)
        | "lung_left" -> Ok (Lung Left)
        | "lung_right" -> Ok (Lung Right)
        | "parotid_left" -> Ok (Parotid Left)
        | "parotid_right" -> Ok (Parotid Right)
        | "femoral_head_left" -> Ok (FemoralHead Left)
        | "femoral_head_right" -> Ok (FemoralHead Right)
        | "spinal_cord" -> Ok SpinalCord
        | "brainstem" -> Ok Brainstem
        | "heart" -> Ok Heart
        | "liver" -> Ok Liver
        | "spleen" -> Ok Spleen
        | "esophagus" -> Ok Esophagus
        | "trachea" -> Ok Trachea
        | "sacrum" -> Ok Sacrum
        | other -> Error (sprintf "Unknown organ key '%s'." other)

    /// Converts a recognized anatomical identity to a canonical key.
    let format =
        function
        | Rectum -> "rectum" | Bladder -> "bladder"
        | Prostate -> "prostate" | SeminalVesicles -> "seminal_vesicles"
        | Uterus -> "uterus"
        | Kidney Left -> "kidney_left" | Kidney Right -> "kidney_right"
        | Lung Left -> "lung_left" | Lung Right -> "lung_right"
        | Parotid Left -> "parotid_left" | Parotid Right -> "parotid_right"
        | FemoralHead Left -> "femoral_head_left" | FemoralHead Right -> "femoral_head_right"
        | SpinalCord -> "spinal_cord" | Brainstem -> "brainstem"
        | Heart -> "heart" | Liver -> "liver" | Spleen -> "spleen"
        | Esophagus -> "esophagus" | Trachea -> "trachea" | Sacrum -> "sacrum"
        | Vertebra _ -> "vertebra"
        | OtherOrgan text -> text

    /// Gets the laterality required by a bilateral anatomical structure.
    let side =
        function
        | Kidney side | Lung side | Parotid side | FemoralHead side -> Some side
        | _ -> None

/// Recognizes source structure IDs and inventories the entire structure set.
module StructureNames =

    /// Removes accents and separators without changing anatomical meaning.
    let compact (value: string) =
        let decomposed = value.Normalize(NormalizationForm.FormD)
        let builder = StringBuilder()
        for character in decomposed do
            if CharUnicodeInfo.GetUnicodeCategory(character) <> UnicodeCategory.NonSpacingMark &&
               Char.IsLetterOrDigit(character) then
                builder.Append(Char.ToUpperInvariant(character)) |> ignore
        builder.ToString()

    /// Splits source identifiers into alphanumeric tokens for exclusion checks.
    let tokens (value: string) =
        Regex.Split(value.ToUpperInvariant(), "[^A-Z0-9]+")
        |> Array.toList
        |> List.filter (String.IsNullOrWhiteSpace >> not)

    /// Parses a vertebra label with a safe optional prefix and an explicit level number.
    let tryVertebra (convention: NamingConvention) (actualId: string) =
        let name = compact actualId
        let prefixes = convention.VertebraPrefixes |> List.map compact |> List.sortByDescending (fun value -> value.Length)
        let abbreviation =
            prefixes
            |> List.tryPick (fun prefix ->
                if name.StartsWith(prefix, StringComparison.Ordinal) then
                    Some (name.Substring(prefix.Length))
                else None)
            |> Option.defaultValue name
        let matched = Regex.Match(abbreviation, "^(C|TH|T|L|S)0?([1-9]|1[0-2])$")
        if not matched.Success then None
        else
            let number = Int32.Parse(matched.Groups.[2].Value, CultureInfo.InvariantCulture)
            let level =
                match matched.Groups.[1].Value with
                | "C" when number <= 7 -> Some (Cervical number)
                | "T" | "TH" when number <= 12 -> Some (Thoracic number)
                | "L" when number <= 5 -> Some (Lumbar number)
                | "S" when number <= 5 -> Some (Sacral number)
                | _ -> None
            level |> Option.map Vertebra

    /// Checks for excluded PRV, optimization, ring, and evaluation modifiers.
    let excludedModifier (dictionary: NamingDictionary) actualId =
        let candidates = tokens actualId
        dictionary.Convention.ExcludedModifierTokens
        |> List.tryFind (fun modifier ->
            let key = compact modifier
            candidates |> List.exists (fun token -> token = key))
        
    /// Checks a base alias with optional leading/trailing laterality.
    let matchesAlias (dictionary: NamingDictionary) organ actualId alias =
        let actual = compact actualId
        let baseName = compact alias
        match OrganIds.side organ with
        | None -> actual = baseName
        | Some side ->
            let suffixes =
                match side with
                | Left -> dictionary.Convention.LeftTokens
                | Right -> dictionary.Convention.RightTokens
            suffixes
            |> List.map compact
            |> List.exists (fun suffix ->
                actual = baseName + suffix || actual = suffix + baseName)

    /// Resolves one name without guessing from partial substrings or fuzzy similarity.
    let resolve (dictionary: NamingDictionary) actualId =
        match excludedModifier dictionary actualId with
        | Some modifier -> ExcludedModifier modifier
        | None ->
            match tryVertebra dictionary.Convention actualId with
            | Some organ -> Recognized organ
            | None ->
                let matches =
                    dictionary.Entries
                    |> List.choose (fun (organ, aliases) ->
                        if aliases |> List.exists (matchesAlias dictionary organ actualId) then Some organ
                        else None)
                    |> List.distinct
                match matches with
                | [] -> Unrecognized
                | [organ] -> Recognized organ
                | organs -> Ambiguous organs

    /// Classifies every source identifier before running structure-set rules.
    let inventory dictionary (ids: string list) =
        let items =
            ids |> List.map (fun id -> { ActualId = id; Match = resolve dictionary id })
        let duplicates =
            items
            |> List.choose (fun item ->
                match item.Match with
                | Recognized organ -> Some (organ, item.ActualId)
                | _ -> None)
            |> List.groupBy fst
            |> List.choose (fun (organ, occurrences) ->
                let names = occurrences |> List.map snd
                if List.length names > 1 then Some (organ, names) else None)
        { Items = items; DuplicateOrgans = duplicates }
