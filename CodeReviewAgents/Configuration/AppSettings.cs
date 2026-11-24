namespace CodeReviewAgents.Configuration;

public sealed class AppSettings
{
    public OpenRouterSettings OpenRouter { get; set; } = new();
    public GitHubSettings GitHub { get; set; } = new();
    public SmtpSettings Smtp { get; set; } = new();
    public ReviewSettings Review { get; set; } = new();
}

public sealed class OpenRouterSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string Model { get; set; } = "openrouter/free";
    public string SiteUrl { get; set; } = "https://github.com/code-review-agents";
    public string AppName { get; set; } = "CodeReviewAgents";
    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class GitHubSettings
{
    public string Token { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public int PullRequestNumber { get; set; }
    public bool DryRun { get; set; } = true;
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "AI Code Review Lead";
    public string ToEmail { get; set; } = string.Empty;
    public bool DryRun { get; set; } = true;
}

public sealed class ReviewSettings
{
    /// <summary>
    /// Auto (LocalGit -> GitHubPr -> Sample), LocalGit, GitHubPr, or Sample.
    /// </summary>
    public string DiffSource { get; set; } = "Auto";
    public string GitDiffArguments { get; set; } = "diff HEAD";
    public string WorkingDirectory { get; set; } = string.Empty;
    public int MaxDiffCharacters { get; set; } = 60_000;
}
