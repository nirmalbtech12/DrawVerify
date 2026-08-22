# test-design prompt
Story: {{jira_key}} — {{jira_summary}}
Requirement: {{requirement}}
ACs:
{{acceptance_criteria}}

Technical Design:
{{technical_design}}

Test Design:
{{test_design}}

Additional:
{{diff}}
{{build_output}}
{{test_output}}

Write the complete markdown document to `.pipeline/state/{{jira_key}}/test-design.md` (and also return it as your final message) containing at minimum the required headings `## Jira`, `## Test Strategy`, `## Unit Tests`, and `## Acceptance Criteria` in addition to the full sections listed in the agent definition. Do not modify source, Jira, or Git.
