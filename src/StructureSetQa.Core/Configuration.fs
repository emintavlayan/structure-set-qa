namespace StructureSetQa

open System
open System.IO
open System.Globalization
open FsToolkit.ErrorHandling
open YamlDotNet.RepresentationModel
open Domain

/// Parses a deliberately small, documented YAML vocabulary into validated domain rules.
module Configuration =

    /// Converts an ordered sequence of results into one result without hiding a bad rule.
    let sequence results =
        results
        |> List.fold (fun state next ->
            result {
                let! collected = state
                let! item = next
                return item :: collected
            }) (Ok [])
        |> Result.map List.rev

    /// Reads one YAML document and identifies invalid syntax by file name.
    let document filename content : Result<YamlNode, string> =
        try
            use reader = new StringReader(content)
            let stream = YamlStream()
            stream.Load(reader)
            if stream.Documents.Count <> 1 then
                Error (sprintf "%s: expected exactly one YAML document." filename)
            else
                Ok stream.Documents.[0].RootNode
        with error ->
            Error (sprintf "%s: YAML syntax error: %s" filename error.Message)

    /// Requires a YAML mapping at a documented location.
    let mapping label (node: YamlNode) =
        match node with
        | :? YamlMappingNode as value -> Ok value
        | _ -> Error (sprintf "%s must be a YAML mapping." label)

    /// Reads a mapping property without assuming it exists.
    let field (map: YamlMappingNode) key =
        map.Children
        |> Seq.tryPick (fun pair ->
            match pair.Key with
            | :? YamlScalarNode as scalar when scalar.Value = key -> Some pair.Value
            | _ -> None)

    /// Rejects misspelled or unsupported configuration keys at the relevant YAML location.
    let allowedKeys label expected (map: YamlMappingNode) =
        let known = Set.ofList expected
        let names =
            map.Children
            |> Seq.map (fun pair ->
                match pair.Key with
                | :? YamlScalarNode as scalar -> scalar.Value
                | _ -> "<nontext-key>")
            |> Seq.toList
        let unexpected = names |> List.filter (fun name -> not (Set.contains name known))
        if List.isEmpty unexpected then Ok ()
        else Error (sprintf "%s: unknown YAML field(s): %s" label (String.concat ", " unexpected))

    /// Requires one mapping property with an explanatory error.
    let required label key map =
        match field map key with
        | Some value -> Ok value
        | None -> Error (sprintf "%s: missing '%s'." label key)

    /// Requires a nonblank scalar string.
    let scalar label (node: YamlNode) =
        match node with
        | :? YamlScalarNode as value when not (String.IsNullOrWhiteSpace value.Value) -> Ok value.Value
        | _ -> Error (sprintf "%s must be nonempty text." label)

    /// Parses a required string property.
    let text label key map =
        result {
            let! node = required label key map
            return! scalar (label + "." + key) node
        }

    /// Parses a positive or zero finite millimetre value.
    let nonnegative label value =
        match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, number when not (Double.IsInfinity number || Double.IsNaN number) && number >= 0.0 -> Ok number
        | _ -> Error (sprintf "%s must be a nonnegative finite number." label)

    /// Parses an optional number property or its documented default.
    let number label key fallback map =
        match field map key with
        | None -> Ok fallback
        | Some node ->
            result {
                let! value = scalar (label + "." + key) node
                return! nonnegative (label + "." + key) value
            }

    /// Parses one required positive integer.
    let positiveInteger label node =
        result {
            let! value = scalar label node
            match Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, count when count > 0 -> return count
            | _ -> return! Error (sprintf "%s must be a positive integer." label)
        }

    /// Parses a one-dimensional list of strings.
    let stringList label (node: YamlNode) =
        match node with
        | :? YamlSequenceNode as items ->
            items.Children
            |> Seq.toList
            |> List.map (scalar label)
            |> sequence
        | _ -> Error (sprintf "%s must be a YAML list." label)

    /// Parses a version and rejects incompatible rulebook schemas.
    let version label map =
        result {
            let! node = required label "version" map
            let! value = positiveInteger (label + ".version") node
            if value = 1 then return value
            else return! Error (sprintf "%s: unsupported version %d." label value)
        }

    /// Parses a severity description used in rule reports.
    let severity label (value: string) =
        match value.ToLowerInvariant() with
        | "information" -> Ok Information
        | "warning" -> Ok Warning
        | "critical" -> Ok Critical
        | _ -> Error (sprintf "%s: unknown severity '%s'." label value)

    /// Parses an explicit coverage prerequisite for spatial rules.
    let coverage label (value: string) =
        match value.ToLowerInvariant() with
        | "observed" -> Ok AnyObservedExtent
        | "complete_candidate" -> Ok (BothClearOfGridEdges 3.0)
        | _ -> Error (sprintf "%s: coverage must be 'observed' or 'complete_candidate'." label)

    /// Parses an anatomical direction keyword.
    let direction label (value: string) =
        match value.ToLowerInvariant() with
        | "left_of" -> Ok LeftOf
        | "right_of" -> Ok RightOf
        | "anterior_to" -> Ok AnteriorTo
        | "posterior_to" -> Ok PosteriorTo
        | "superior_to" -> Ok SuperiorTo
        | "inferior_to" -> Ok InferiorTo
        | _ -> Error (sprintf "%s: unsupported direction '%s'." label value)

    /// Parses a canonical organ key, including additional local identities.
    let organ label (key: string) =
        OrganIds.parse key |> Result.mapError (fun reason -> sprintf "%s: %s" label reason)

    /// Parses one required organ property from an SS-level rule.
    let organField label key map =
        result {
            let! name = text label key map
            return! organ (label + "." + key) name
        }

    /// Reads a YAML sequence of mapping entries.
    let mappingRows label (node: YamlNode) =
        match node with
        | :? YamlSequenceNode as entries ->
            entries.Children
            |> Seq.mapi (fun index item -> mapping (sprintf "%s[%d]" label index) item)
            |> Seq.toList
            |> sequence
        | _ -> Error (sprintf "%s must be a list of rule mappings." label)

    /// Converts the configured source naming conventions into a typed dictionary.
    let naming content : Result<NamingDictionary, string> =
        result {
            let! root = document "structure-ids.yaml" content
            let! settings = mapping "structure-ids.yaml" root
            let! _ = version "structure-ids.yaml" settings
            do! allowedKeys "structure-ids.yaml" ["version"; "laterality"; "excluded_modifiers"; "vertebra_prefixes"; "structures"] settings
            let! sidesNode = required "structure-ids.yaml" "laterality" settings
            let! sides = mapping "laterality" sidesNode
            do! allowedKeys "laterality" ["left"; "right"] sides
            let! leftNode = required "laterality" "left" sides
            let! rightNode = required "laterality" "right" sides
            let! leftTokens = stringList "laterality.left" leftNode
            let! rightTokens = stringList "laterality.right" rightNode
            let! excluded = required "structure-ids.yaml" "excluded_modifiers" settings
            let! excludedTokens = stringList "excluded_modifiers" excluded
            let! prefixes = required "structure-ids.yaml" "vertebra_prefixes" settings
            let! vertebraPrefixes = stringList "vertebra_prefixes" prefixes
            let! entriesNode = required "structure-ids.yaml" "structures" settings
            let! entriesMap = mapping "structures" entriesNode
            let! entries =
                entriesMap.Children
                |> Seq.map (fun pair ->
                    result {
                        let! key = scalar "structures key" pair.Key
                        let! value = organ "structures" key
                        let! aliases = stringList ("structures." + key) pair.Value
                        if List.isEmpty aliases then
                            return! Error (sprintf "structures.%s must have an alias." key)
                        return value, aliases
                    })
                |> Seq.toList
                |> sequence

            let convention = {
                LeftTokens = leftTokens
                RightTokens = rightTokens
                ExcludedModifierTokens = excludedTokens
                VertebraPrefixes = vertebraPrefixes
            }
            let dictionary = { Convention = convention; Entries = entries }
            let duplicateSides =
                Set.intersect
                    (leftTokens |> List.map StructureNames.compact |> Set.ofList)
                    (rightTokens |> List.map StructureNames.compact |> Set.ofList)
            if not (Set.isEmpty duplicateSides) then
                return! Error "Laterality suffixes overlap between left and right."
            return dictionary
        }

    /// Converts per-organ topology and scan-boundary screening rules.
    let geometry (content: string) : Result<(Organ * StructureRuleDefinition list) list, string> =
        result {
            let! root = document "geometry.yaml" content
            let! settings = mapping "geometry.yaml" root
            let! _ = version "geometry.yaml" settings
            do! allowedKeys "geometry.yaml" ["version"; "defaults"; "structures"] settings
            let! defaultsNode = required "geometry.yaml" "defaults" settings
            let! defaults = mapping "geometry.defaults" defaultsNode
            do! allowedKeys "geometry.defaults" ["scan_edge_margin_mm"] defaults
            let! margin = number "geometry.defaults" "scan_edge_margin_mm" 3.0 defaults
            let! structuresNode = required "geometry.yaml" "structures" settings
            let! structures = mapping "geometry.structures" structuresNode
            return!
                structures.Children
                |> Seq.map (fun pair ->
                    result {
                        let! key = scalar "geometry organ" pair.Key
                        let! identity = organ "geometry" key
                        let! settings = mapping ("geometry." + key) pair.Value
                        do! allowedKeys ("geometry." + key) ["max_connected_parts"; "scan_edge_margin_mm"] settings
                        let! parts =
                            match field settings "max_connected_parts" with
                            | None -> Ok []
                            | Some node ->
                                positiveInteger ("geometry." + key + ".max_connected_parts") node
                                |> Result.map (fun count ->
                                    let rule : StructureRuleDefinition = {
                                        Id = key + ".connected-parts"
                                        Severity = Warning
                                        Rule = Geometry (MaximumSeparateParts count)
                                    }
                                    [rule])
                        let! edge = number ("geometry." + key) "scan_edge_margin_mm" margin settings
                        let checks : StructureRuleDefinition = {
                            Id = key + ".scan-edge"
                            Severity = Information
                            Rule = Geometry (ReportGridEdgeProximity edge)
                        }
                        return identity, parts @ [checks]
                    })
                |> Seq.toList
                |> sequence
        }

    /// Converts one clinician-written relationship into a typed SS-level rule.
    let relationshipRow (row: YamlMappingNode) : Result<StructureSetRuleDefinition, string> =
        result {
            let! id = text "relationship" "id" row
            let! kind = text id "kind" row
            let expected =
                match kind with
                | "relative_position" -> ["id"; "kind"; "subject"; "reference"; "direction"; "minimum_mm"; "coverage"; "severity"]
                | "between_centres" -> ["id"; "kind"; "subject"; "first"; "second"; "axis"; "margin_mm"; "coverage"; "severity"]
                | _ -> []
            if not (List.isEmpty expected) then do! allowedKeys id expected row
            let! subject = organField id "subject" row
            let! severityName = text id "severity" row
            let! priority = severity id severityName
            let! coverageName = text id "coverage" row
            let! needed = coverage id coverageName
            let! rule =
                match kind with
                | "relative_position" ->
                    result {
                        let! reference = organField id "reference" row
                        let! directionName = text id "direction" row
                        let! relation = direction id directionName
                        let! distance = number id "minimum_mm" 0.0 row
                        return RelativePosition {
                            Subject = subject
                            Reference = reference
                            Direction = relation
                            Measure = StructureCenterPoint
                            MinimumDifferenceMm = distance
                            Coverage = needed
                        }
                    }
                | "between_centres" ->
                    result {
                        let! first = organField id "first" row
                        let! second = organField id "second" row
                        let! axisText = text id "axis" row
                        let axis = axisText.ToLowerInvariant()
                        if axis <> "x" && axis <> "y" && axis <> "z" then
                            return! Error (sprintf "%s: axis must be x, y, or z." id)
                        let! margin = number id "margin_mm" 0.0 row
                        return BetweenCentres {
                            Subject = subject
                            First = first
                            Second = second
                            Axis = Char.ToUpperInvariant(axis.[0])
                            MarginMm = margin
                            Coverage = needed
                        }
                    }
                | _ -> Error (sprintf "%s: unsupported relationship kind '%s'." id kind)
            return { Id = id; Severity = priority; Rule = rule }
        }

    /// Converts the structure-set relationship rulebook.
    let relationships content =
        result {
            let! root = document "relationships.yaml" content
            let! settings = mapping "relationships.yaml" root
            let! _ = version "relationships.yaml" settings
            do! allowedKeys "relationships.yaml" ["version"; "rules"] settings
            let! listNode = required "relationships.yaml" "rules" settings
            let! rows = mappingRows "relationships.rules" listNode
            return! rows |> List.map relationshipRow |> sequence
        }

    /// Parses a labeled vertebra in a clinician-written list.
    let vertebralLevel label name =
        let defaults = { LeftTokens = []; RightTokens = []; ExcludedModifierTokens = []; VertebraPrefixes = [] }
        match StructureNames.tryVertebra defaults name with
        | Some (Vertebra level) -> Ok level
        | _ -> Error (sprintf "%s: invalid vertebral level '%s'." label name)

    /// Converts one nearest-vertebra rule into a structure-level assertion.
    let vertebralRow (row: YamlMappingNode) : Result<Organ * StructureRuleDefinition, string> =
        result {
            let! id = text "vertebral rule" "id" row
            do! allowedKeys id ["id"; "subject"; "allowed_levels"; "coverage"; "severity"] row
            let! subject = organField id "subject" row
            let! severityName = text id "severity" row
            let! priority = severity id severityName
            let! allowedNode = required id "allowed_levels" row
            let! names = stringList (id + ".allowed_levels") allowedNode
            let! levels = names |> List.map (vertebralLevel id) |> sequence
            if List.isEmpty levels then return! Error (id + ": allowed_levels cannot be empty.")
            let! coverageName = text id "coverage" row
            let! needed = coverage id coverageName
            return subject, {
                Id = id
                Severity = priority
                Rule = Vertebral (NearestVertebraAllowed (levels, needed))
            }
        }

    /// Converts the vertebral-center screening rulebook.
    let vertebrae content =
        result {
            let! root = document "vertebrae.yaml" content
            let! settings = mapping "vertebrae.yaml" root
            let! _ = version "vertebrae.yaml" settings
            do! allowedKeys "vertebrae.yaml" ["version"; "rules"] settings
            let! listNode = required "vertebrae.yaml" "rules" settings
            let! rows = mappingRows "vertebrae.rules" listNode
            return! rows |> List.map vertebralRow |> sequence
        }

    /// Rejects duplicate identifiers that would make findings hard to audit.
    let validateUniqueRuleIds (knowledge: QaKnowledge) =
        let names =
            (knowledge.Structures |> List.collect (fun item -> item.Rules |> List.map (fun rule -> rule.Id))) @
            (knowledge.StructureSetRules |> List.map (fun rule -> rule.Id))
        let duplicates =
            names |> List.countBy id |> List.filter (fun (_, count) -> count > 1) |> List.map fst
        if List.isEmpty duplicates then Ok knowledge
        else Error (sprintf "Duplicate rule IDs: %s" (String.concat ", " duplicates))

    /// Parses four rulebooks together and returns one typed knowledge model.
    let parse namesText geometryText relationsText vertebraText : Result<QaKnowledge, string> =
        result {
            let! namingDictionary = naming namesText
            let! intrinsic = geometry geometryText
            let! relations = relationships relationsText
            let! vertebral = vertebrae vertebraText

            let dictionaryOrgans = namingDictionary.Entries |> List.map fst
            let configuredOrgans =
                (intrinsic |> List.map fst) @ (vertebral |> List.map fst)
            let definitions =
                dictionaryOrgans @ configuredOrgans
                |> List.distinct
                |> List.map (fun identity ->
                    let aliases =
                        namingDictionary.Entries
                        |> List.tryPick (fun (organ, values) -> if identity = organ then Some values else None)
                        |> Option.defaultValue []
                    let rules =
                        (intrinsic |> List.filter (fun (organ, _) -> organ = identity) |> List.collect snd) @
                        (vertebral |> List.filter (fun (organ, _) -> organ = identity) |> List.map snd)
                    { Organ = identity; Aliases = aliases; Rules = rules })
            let result = {
                Version = 1
                Naming = namingDictionary
                Structures = definitions
                StructureSetRules = relations
            }
            return! validateUniqueRuleIds result
        }
