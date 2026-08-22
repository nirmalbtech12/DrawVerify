namespace LEMUAgenticPipeline.Services;

/// <summary>
/// Build and test execution service — deterministic, not AI-controlled.
/// Wraps `dotnet build` and `dotnet test` with proper working directory and error handling.
/// Build/test failures are NOT auto-repaired in the initial MVP.
/// </summary>
public interface IBuildTestService
{
    /// <summary>Execute `dotnet build` for the solution.</summary>
    /// <param name="solutionPath">Path to .sln file.</param>
    /// <param>configuration">Build configuration (Debug/Release).</param>
    /// <returns>Build result with success/failure and output.</returns>
    Task<BuildResult> BuildAsync(string solutionPath, string configuration = "Debug");

    /// <summary>Execute `dotnet test` for the test project.</summary>
    /// <param name="projectPath">Path to test project .csproj.</param>
    /// <param name="configuration">Test configuration (Debug/Release).</param>
    /// <returns>Test result with success/failure and output.</returns>
    Task<TestResult> TestAsync(string projectPath, string configuration = "Debug");

    /// <summary>Determine if a build/test failure belongs to the current story.</summary>
    /// <param name="failureOutput">Output from build/test execution.</param>
    /// <returns>True if the failure is story-related (not a pre-existing issue).</returns>
    bool IsStoryRelatedFailure(string failureOutput);
}

/// <summary>Result of a `dotnet build` execution.</summary>
public sealed class BuildResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? ErrorOutput { get; set; }
    public string? LogFilePath { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>Result of a `dotnet test` execution.</summary>
public sealed class TestResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? ErrorOutput { get; set; }
    public int TotalCount { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public double Duration { get; set; }
    public string? DotNetTestLog { get; set; }
}