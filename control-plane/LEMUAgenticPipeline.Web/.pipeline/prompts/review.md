# review prompt
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

Produce output per .opencode/agents/review schema. Do not modify source, Jira, or Git.
