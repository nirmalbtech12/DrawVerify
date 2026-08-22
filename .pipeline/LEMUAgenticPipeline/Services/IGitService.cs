namespace LEMUAgenticPipeline.Services;

/// <summary>
/// Git integration service — abstracts Git CLI operations.
/// The orchestrator uses this for branch management and status checks.
/// Git branch creation is orchestrator-only; agents must NOT modify Git state.
/// </summary>
public interface IGitService
{
    /// <summary>Check if working tree is clean (no uncommitted changes).</summary>
    Task<bool> IsWorkingTreeCleanAsync();

    /// <summary>Get current branch name.</summary>
    Task<string> GetCurrentBranchAsync();

    /// <summary>Switch to a specified branch.</summary>
    Task SwitchBranchAsync(string branchName);

    /// <summary>Pull from remote (origin/main).</summary>
    Task PullAsync();

    /// <summary>Create a new branch from current HEAD.</summary>
    /// <param name="branchName">Desired branch name (typically feature/<JIRA-ID>-<short-name>).</param>
    Task CreateBranchAsync(string branchName);

    /// <summary>Get the current diff (changes since last commit).</summary>
    Task<string> GetDiffAsync();

    /// <summary>Stage specific files for commit.</summary>
    Task AddFilesAsync(params string[] filePaths);

    /// <summary>Commit staged changes with a message.</summary>
    /// <param name="message">Commit message.</param>
    Task CommitAsync(string message);

    /// <summary>Push the current branch to origin.</summary>
    Task PushAsync();

    /// <summary>Create a GitHub PR.</summary>
    /// <param name="title">PR title (e.g., "DV-TEST-001: Validate imported geometry").</param>
    /// <param name="body">PR body markdown.</param>
    Task CreatePrAsync(string title, string body);

    /// <summary>Check if a PR already exists for this branch.</summary>
    Task<bool> PrExistsAsync(string prTitle);
}