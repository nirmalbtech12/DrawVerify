using LEMUAgenticPipeline.Models;

namespace LEMUAgenticPipeline.Services;

/// <summary>
/// Jira integration service — abstracts Atlassian MCP or REST API access.
/// The orchestrator uses this to retrieve Epic/Feature/Story hierarchy and acceptance criteria.
/// Must NOT alter Jira requirements or descriptions (safety rule).
/// </summary>
public interface IJiraService
{
    /// <summary>Retrieve a Jira issue by key (e.g., DV-TEST-001).</summary>
    /// <param name="issueKey">Jira issue key.</param>
    /// <returns>StoryContext populated with Jira data.</returns>
    Task<StoryContext> GetStoryAsync(string issueKey);

    /// <summary>Retrieve the parent Epic for a story.</summary>
    Task<StoryContext> GetEpicAsync(string epicKey);

    /// <summary>Retrieve the parent Feature for a story.</summary>
    Task<StoryContext> GetFeatureAsync(string featureKey);

    /// <summary>Validate that the story hierarchy is intact (Epic → Feature → Story).</summary>
    Task<bool> ValidateHierarchyAsync(string storyKey);

    /// <summary>Add a comment to a Jira issue (orchestrator-only operation).</summary>
    /// <param name="issueKey">Target issue key.</param>
    /// <param name="commentBody">Comment text.</param>
    Task AddCommentAsync(string issueKey, string commentBody);

    /// <summary>Transition a Jira issue to a new status.</summary>
    /// <param name="issueKey">Target issue key.</param>
    /// <param name="transitionName">Transition name (e.g., "In Progress", "Done").</param>
    Task TransitionIssueAsync(string issueKey, string transitionName);

    /// <summary>Update a single Jira field (e.g., customfield_10168) with markdown/text. Used for approved design sync.</summary>
    /// <param name="issueKey">Target issue key.</param>
    /// <param name="fieldId">Field ID (e.g., customfield_10168).</param>
    /// <param name="value">Markdown/text value to store.</param>
    Task UpdateIssueFieldAsync(string issueKey, string fieldId, string value);
}