<<<SYSTEM_PROMPT>>>
You are an AI assistant participating in the DrawVerify Agentic Development Pipeline.

Deterministic orchestration, AI-assisted reasoning, human-controlled approval.
- You never control workflow state, Git operations, Jira, merges, or deployments.
- You only act when explicitly invoked for a specific agent task.
- You never bypass approval gates.
- Do not commit, push, create branches, or create PRs.
- Do not change Jira issues, workflows, or requirements.
- Do not execute builds or tests.
- Produce output strictly in the format defined for your role.
- Map every acceptance criterion explicitly (AC-01...).
- Highlight OCCT/PInvoke safety, error handling, and backward-compat concerns.
- When your role requires a structured document (e.g., Technical Designer, Test Designer), output ONLY that document in the required format. Do not add chat-style commentary outside the document.

<<<SYSTEM_PROMPT_END>>>

<<<TASK_PROMPT>>>
Create a Technical Design Detail for story KAN-20.

You are the Technical Designer. Produce a complete Technical Design Detail in Markdown that will be validated automatically. Missing required headings will cause pipeline failure.

=== STRICT OUTPUT RULES (MUST FOLLOW) ===
- Output ONLY the technical design document.
- Do NOT include any text before the first heading or after the last heading.
- Do NOT include any chat-style commentary such as:
  - "Now I have a comprehensive understanding..."
  - "Let me create the technical design document."
  - "Here is the design."
  - Any other meta-text, introductions, summaries, or explanations.
- Start directly with: `## Jira`
- If you are tempted to write an introduction or summary, omit it and write the document instead.

=== REQUIRED HEADINGS (MINIMUM) ===
Your document MUST contain at least these exact headings:

- ## Jira
- ## Requirement
- ## Proposed Changes
- ## Risks
- ## Acceptance Criteria Mapping

=== FULL STRUCTURE (RECOMMENDED) ===
For robustness, include the full structure in this order:

# Technical Design
## Jira
## Requirement
## Acceptance Criteria
## Existing Architecture
## Existing Code
## Proposed Changes
## Files Expected To Change
## API / Interface Changes
## Database Changes
## OCCT Changes
## Error Handling
## Backward Compatibility
## Testing Considerations
## Risks
## Implementation Steps
## Acceptance Criteria Mapping
## Uncertainties

=== INPUTS ===
Jira Key: KAN-20
Summary: US-1.1.2: Extract STEP measurements
Requirement:
As a Maker, I want the system to extract key measurements (volume, bounding box, feature dimensions) from the STEP model so verification can be performed.
Acceptance Criteria:
1. AC-01 — Trigger measurement extraction
Given a 
Document
 with 
DocumentType = StepModel
 and 
UploadStatus = Uploaded
 exists,
When a Maker requests “Extract Measurements” for that document,
Then the system invokes the geometry service to load the STEP file and compute measurements,
And updates 
GeometryStatus
 to 
Processed
 on success (or 
Error
 on failure). AC-02 — Basic geometric measurements
Given a valid STEP model is loaded,
When measurement extraction runs,
Then the system computes at minimum: Volume (if the model represents a solid)
Bounding box (min/max X, Y, Z)
Count of faces, edges, and vertices
And stores these results in a 
GeometryData
 (or equivalent) record linked to the document. AC-03 — Feature-level dimensions (where detectable)
Given the STEP model contains detectable features (e.g., holes, bosses, slots),
When measurement extraction runs,
Then the system extracts feature-level dimensions where possible (e.g., hole diameters, boss heights, slot widths),
And records them as 
FeatureData
 entries with: Feature type (e.g., 
Hole
, 
Boss
, 
Slot
)
Nominal dimension value(s)
Optional tolerance fields (if present in the model or derived from metadata)
And links them to the document. (Note: For MVP, “where possible” means: if the OCCT-based service can reliably identify these features; unsupported features are listed under uncertainties or ignored without crashing.) AC-04 — Invalid or non-solid geometry handling
Given the STEP file does not represent a valid solid (e.g., only surfaces, wires, or corrupted geometry),
When measurement extraction runs,
Then the system: Does not crash,
Sets 
GeometryStatus = Error
,
Records an error message (e.g., “Model does not contain a valid solid; volume cannot be computed.”),
Still stores any partial measurements that could be computed (e.g., bounding box, face/edge/vertex counts) if available. AC-05 — Performance and timeout
Given a STEP model up to the configured maximum size (e.g., 50 MB),
When measurement extraction runs,
Then the operation completes within a reasonable time (e.g., ≤ 30 seconds for typical models),
Or fails gracefully with a timeout error and sets 
GeometryStatus = Error
 if the operation exceeds the configured timeout. AC-06 — Measurement results visibility
Given measurement extraction has completed successfully (
GeometryStatus = Processed
),
When a user views the document details,
Then they can see: Volume (if applicable)
Bounding box dimensions
Face/edge/vertex counts
Detected features and their dimensions (if any)
And these values are clearly labeled as derived from the STEP model. AC-07 — Audit logging of extraction
Given measurement extraction is performed,
When the operation completes (success or failure),
Then an audit entry is created in 
DocumentAuditLog
 with: DocumentId
Action
 = “MeasurementsExtracted” or “MeasurementExtractionFailed”
UserId
 (who triggered it)
Timestamp
DetailsJson
 (including duration, error message if any, and summary of computed measurements)
And audit records are retained indefinitely for MVP. AC-08 — Security & access control
Given a user is not authenticated or does not have the “Maker” or “Checker” role,
When they try to trigger measurement extraction or view measurement results,
Then they are denied access (HTTP 401/403 or redirected to login),
And no measurement operation is performed. AC-09 — Idempotency and re-extraction
Given measurements have already been extracted for a document (
GeometryStatus = Processed
),
When a Maker requests “Extract Measurements” again,
Then the system: Re-runs the extraction,
Overwrites previous 
GeometryData
 / 
FeatureData
 for that document version,
Logs an audit entry for the re-extraction,
Does not create duplicate document versions solely due to re-extraction. AC-10 — Error reporting and user feedback
Given measurement extraction fails (e.g., invalid geometry, timeout, OCCT error),
When the operation completes,
Then the user sees a clear error message summarizing the failure (e.g., “Measurement extraction failed: model does not contain a valid solid.”),
And the document details view indicates 
GeometryStatus = Error
 with the stored error message visible.
Epic: 
Feature: KAN-6

Technical Design (previous, if any):
(not yet approved)

Test Design (previous, if any):
(not yet approved)

=== INSTRUCTIONS ===
- Use the inputs above and repository context (AGENTS.md, existing code) to fill every section.
- Trace every AC (AC-01...) in ## Acceptance Criteria Mapping.
- Flag OCCT/PInvoke safety, resource handling, and backward-compat concerns.
- If any information is missing or unclear, still produce the full document and list uncertainties under `## Uncertainties`.

=== FILE ARTIFACT (MANDATORY) ===
You MUST write the complete document to the file:
  .pipeline/state/KAN-20/technical-design.md

Use the file tool to do this. The file content must be IDENTICAL to your stdout output.

=== FINAL REMINDER ===
Output ONLY the technical design document. Do not include any other text.

<<<TASK_PROMPT_END>>>

<<<ORCHESTRATOR_METADATA>>>
Model: opencode/nemotron-3.5-lightning-free
Story: KAN-20
Agent: TechnicalDesigner
TechnicalDesignApproved: False
TestDesignApproved: False
<<<ORCHESTRATOR_METADATA_END>>>