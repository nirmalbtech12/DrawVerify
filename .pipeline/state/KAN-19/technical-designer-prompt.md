<<<SYSTEM_PROMPT>>>
You are an AI assistant participating in the DrawVerify Agentic Development Pipeline.

Deterministic orchestration, AI-assisted reasoning, human-controlled approval.
- You never control workflow state, Git operations, Jira, merges, or deployments.
- You only act when explicitly invoked for a specific agent task.
- You never bypass approval gates.
- Do not commit, push, create branches, or create PRs.
- Do not change Jira issues, workflows, or requirements.
- Do not execute builds or tests.
- Produce output strictly as defined in your Agent.md — final Project Response must be exactly `<response>{ "status": "SUCCESS|FAILURE", "response": "...", "reason": "..." }</response>` with valid JSON inside. No alternative format.
- Map every acceptance criterion explicitly (AC-01...).
- Highlight OCCT/PInvoke safety, error handling, and backward-compat concerns.

<<<SYSTEM_PROMPT_END>>>

<<<TASK_PROMPT>>>
Create Technical Design Detail for story KAN-19 — US-1.1.1: Upload STEP file.

Requirement:
As a Maker, I want to upload a STEP file so the system can read the 3D model geometry.

Acceptance Criteria:
1. AC-01 — Valid STEP file upload
Given I am on the “Upload Model” page,
When I select a valid 
.step
 or 
.stp
 file and confirm the upload,
Then the file is uploaded successfully,
And the system creates a new 
Document
 record with: DocumentType = StepModel
UploadStatus = Uploaded
GeometryStatus = Pending Validation
And I see a success message showing the document name (original file name) and file size in bytes. AC-02 — File type validation
Given I am on the “Upload Model” page,
When I select a file that is not a STEP file (e.g., 
.dxf
, 
.txt
, 
.pdf
),
Then the upload is rejected,
And I see a clear error message:
“Only STEP files (.step, .stp) are allowed.” AC-03 — File size limit enforcement
Given the configured maximum STEP file size is 
N
 MB (MVP default: 50 MB, integer MB, from config),
When I attempt to upload a STEP file larger than 
N
 MB,
Then the upload is rejected,
And I see an error message:
“File exceeds the maximum allowed size of N MB.” AC-04 — Invalid/corrupt STEP file detection
Given I upload a file with a 
.step
 or 
.stp
 extension,
When the file is corrupt or not a valid STEP format (as detected by a basic format check; full OCCT-based validation is deferred to US-1.1.2),
Then the upload is rejected,
And I see an error message:
“The uploaded STEP file is invalid or corrupt. Please check the file and try again.” (Note: For MVP, this can be a lightweight magic-number / header check; strict OCCT parsing is not required in this story.) AC-05 — Required metadata capture
Given I upload a valid STEP file,
When the upload completes,
Then the system records at minimum: Original file name
File size in bytes
Upload timestamp
Uploader identity (UserId and snapshot of display name)
DocumentType = StepModel
UploadStatus = Uploaded
GeometryStatus = Pending Validation
And this metadata is visible in the model/document details view. AC-06 — Duplicate file handling (MVP policy)
Given a 
Document
 with the same 
(OriginalFileName, FileSize)
 already exists (global scope; project scoping deferred),
When I attempt to upload the same STEP file again,
Then the system: Detects the duplicate based on 
(FileName, FileSize)
 (no checksum for MVP),
Blocks the upload and shows a confirmation dialog:
“A file with the same name and size already exists. Create a new version?”
And if I confirm:
A new version of the document is created (previous file retained on disk),
An audit entry is logged for the duplicate/overwrite event.
And if I cancel:
The upload is aborted,
No new document or version is created. (Duplicate policy for MVP: 
BlockAndConfirm
 only; config-driven, no per-project scoping yet.) AC-07 — Upload progress and cancellation
Given I am uploading a STEP file,
When the upload is in progress,
Then I see a progress indicator (percentage or progress bar) based on bytes copied,
And I can cancel the upload,
And if cancelled: No 
Document
 record is created,
Any partially written file is deleted or left orphaned for cleanup,
I see a message: “Upload cancelled.” (For small/local files, progress may complete almost instantly; that is acceptable.) AC-08 — Security & access control
Given I am not authenticated or do not have the “Maker” role,
When I try to access the upload endpoint or page,
Then I am denied access (HTTP 401/403 or redirected to login),
And no file upload is accepted. AC-09 — File storage and sanitization (new, explicit)
Given a valid STEP file is uploaded,
When the file is stored on disk,
Then: The physical file name is 
{guid}_{sanitizedFileName}
 in the 
uploads/step/
 folder,
The original file name is preserved in the 
Document
 record (
OriginalFileName
),
Invalid path characters are sanitized, and length limits are enforced. AC-10 — Audit logging (new, explicit)
Given a STEP file is uploaded (success, duplicate, or cancelled),
When the operation completes,
Then an audit entry is created in 
DocumentAuditLog
 with: DocumentId
 (if created), 
Action
 (e.g., “Uploaded”, “DuplicateDetected”, “Overwritten”, “Cancelled”), 
UserId
, 
Timestamp
, 
DetailsJson
,
And audit records are retained indefinitely for MVP.

Epic: 
Feature: KAN-6

Previous Technical Design:
(not yet approved)

Previous Test Design:
(not yet approved)

Task: Produce the complete Technical Design Detail per Agent.md data contract. Use repository context (AGENTS.md, existing code, architecture) to fill every section, trace every AC in Acceptance Criteria Mapping, flag OCCT/PInvoke safety, resource handling, and backward-compat concerns, and list uncertainties if information is missing.

<<<TASK_PROMPT_END>>>

<<<ORCHESTRATOR_METADATA>>>
Model: opencode/muse-spark-1.2-contributor-free
Story: KAN-19
Agent: TechnicalDesigner
TechnicalDesignApproved: False
TestDesignApproved: False
<<<ORCHESTRATOR_METADATA_END>>>