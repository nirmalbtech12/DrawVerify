Create a Technical Design Detail for story {{jira_key}}.

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
Jira Key: {{jira_key}}
Summary: {{jira_summary}}
Requirement:
{{requirement}}
Acceptance Criteria:
{{acceptance_criteria}}
Epic: {{epic}}
Feature: {{feature}}

Technical Design (previous, if any):
{{technical_design}}

Test Design (previous, if any):
{{test_design}}

=== INSTRUCTIONS ===
- Use the inputs above and repository context (AGENTS.md, existing code) to fill every section.
- Trace every AC (AC-01...) in ## Acceptance Criteria Mapping.
- Flag OCCT/PInvoke safety, resource handling, and backward-compat concerns.
- If any information is missing or unclear, still produce the full document and list uncertainties under `## Uncertainties`.

=== FILE ARTIFACT (MANDATORY) ===
You MUST write the complete document to the file:
  .pipeline/state/{{jira_key}}/technical-design.md

Use the file tool to do this. The file content must be IDENTICAL to your stdout output.

=== FINAL REMINDER ===
Output ONLY the technical design document. Do not include any other text.
