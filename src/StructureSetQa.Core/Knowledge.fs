namespace StructureSetQa

open Domain

/// Declares initial clinical rules independently of Eclipse and their future YAML representation.
module Knowledge =

    /// Creates a named intrinsic rule.
    let own id severity rule = { Id = id; Severity = severity; Rule = Intrinsic rule }

    /// Creates a named anatomical relationship rule.
    let relative id severity other direction coverage minimumDifferenceMm =
        { Id = id
          Severity = severity
          Rule = Relative {
              Other = other
              Direction = direction
              Measure = StructureCenterPoint
              MinimumDifferenceMm = minimumDifferenceMm
              Coverage = coverage } }

    /// Creates a named, currently optional vertebral-position rule.
    let vertebral id severity window =
        { Id = id; Severity = severity; Rule = Vertebral (CentreWithin window) }

    /// Defines initial anatomical rules using provisional, configurable structure IDs.
    let dayOne : QaKnowledge =
        { Version = 1
          Structures = [
            { Organ = Rectum
              SourceIds = [ "Rectum" ]
              Rules = [
                own "rectum.parts" Warning (MaximumSeparateParts 1)
                own "rectum.grid-edge" Information (ReportGridEdgeProximity 3.0)
                relative "rectum.posterior-bladder" Warning Bladder PosteriorTo (BothClearOfGridEdges 3.0) 5.0
                relative "rectum.anterior-sacrum" Warning Sacrum AnteriorTo (BothClearOfGridEdges 3.0) 5.0
                vertebral "rectum.below-l1" Information
                    { From = Lumbar 1; Through = Sacral 5; MarginMm = 30.0 }
              ] }
            { Organ = Bladder
              SourceIds = [ "Bladder" ]
              Rules = [
                own "bladder.parts" Warning (MaximumSeparateParts 1)
                own "bladder.grid-edge" Information (ReportGridEdgeProximity 3.0)
                relative "bladder.anterior-sacrum" Warning Sacrum AnteriorTo (BothClearOfGridEdges 3.0) 5.0
              ] }
            { Organ = Kidney Left
              SourceIds = [ "Kidney_L" ]
              Rules = [
                own "kidney-left.parts" Warning (MaximumSeparateParts 1)
                own "kidney-left.grid-edge" Information (ReportGridEdgeProximity 3.0)
                relative "kidney-left.left-of-right" Warning (Kidney Right) LeftOf AnyObservedExtent 10.0
                relative "kidney-left.above-rectum" Warning Rectum SuperiorTo (BothClearOfGridEdges 3.0) 20.0
                relative "kidney-left.above-bladder" Warning Bladder SuperiorTo (BothClearOfGridEdges 3.0) 20.0
                vertebral "kidney-left.level" Information
                    { From = Thoracic 11; Through = Lumbar 4; MarginMm = 25.0 }
              ] }
            { Organ = Kidney Right
              SourceIds = [ "Kidney_R" ]
              Rules = [
                own "kidney-right.parts" Warning (MaximumSeparateParts 1)
                own "kidney-right.grid-edge" Information (ReportGridEdgeProximity 3.0)
                relative "kidney-right.above-rectum" Warning Rectum SuperiorTo (BothClearOfGridEdges 3.0) 20.0
                relative "kidney-right.above-bladder" Warning Bladder SuperiorTo (BothClearOfGridEdges 3.0) 20.0
              ] }
            { Organ = Lung Left
              SourceIds = [ "Lung_L" ]
              Rules = [
                own "lung-left.parts" Warning (MaximumSeparateParts 1)
                own "lung-left.grid-edge" Information (ReportGridEdgeProximity 3.0)
                relative "lung-left.left-of-right" Warning (Lung Right) LeftOf AnyObservedExtent 20.0
                relative "lung-left.above-kidney" Warning (Kidney Left) SuperiorTo (BothClearOfGridEdges 3.0) 20.0
              ] }
            { Organ = Lung Right
              SourceIds = [ "Lung_R" ]
              Rules = [
                own "lung-right.parts" Warning (MaximumSeparateParts 1)
                own "lung-right.grid-edge" Information (ReportGridEdgeProximity 3.0)
                relative "lung-right.above-kidney" Warning (Kidney Right) SuperiorTo (BothClearOfGridEdges 3.0) 20.0
              ] }
            { Organ = Sacrum
              SourceIds = [ "Sacrum" ]
              Rules = [
                own "sacrum.parts" Warning (MaximumSeparateParts 1)
              ] }
          ] }
