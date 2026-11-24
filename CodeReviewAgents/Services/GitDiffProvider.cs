using System.Diagnostics;
using System.Text;
using CodeReviewAgents.Configuration;
using Octokit;

namespace CodeReviewAgents.Services;

public sealed class GitDiffProvider
{
    private readonly AppSettings _settings;

    public GitDiffProvider(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<(string Diff, string Source)> GetDiffAsync(CancellationToken cancellationToken = default)
    {
        var mode = _settings.Review.DiffSource?.Trim() ?? "Auto";

        if (mode.Equals("LocalGit", StringComparison.OrdinalIgnoreCase))
        {
            var localDiff = await TryGetLocalGitDiffAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(localDiff))
            {
                return (localDiff, $"Local git ({_settings.Review.GitDiffArguments})");
            }

            throw new InvalidOperationException("DiffSource 'LocalGit' seçildi ancak yerel git diff boş döndü.");
        }

        if (mode.Equals("GitHubPr", StringComparison.OrdinalIgnoreCase))
        {
            var prDiff = await TryGetGitHubPrDiffAsync();
            if (!string.IsNullOrWhiteSpace(prDiff))
            {
                return (prDiff, $"GitHub PR #{_settings.GitHub.PullRequestNumber} ({_settings.GitHub.Owner}/{_settings.GitHub.Repo})");
            }

            throw new InvalidOperationException("DiffSource 'GitHubPr' seçildi ancak PR diff alınamadı.");
        }

        if (mode.Equals("Sample", StringComparison.OrdinalIgnoreCase))
        {
            return (SampleDiff, "Built-in Sample Git Diff");
        }

        // Auto mode: try GitHub PR (if configured) -> Local git -> Built-in sample diff
        if (!string.IsNullOrWhiteSpace(_settings.GitHub.Token)
            && !string.IsNullOrWhiteSpace(_settings.GitHub.Owner)
            && !string.IsNullOrWhiteSpace(_settings.GitHub.Repo)
            && _settings.GitHub.PullRequestNumber > 0)
        {
            var prDiff = await TryGetGitHubPrDiffAsync();
            if (!string.IsNullOrWhiteSpace(prDiff))
            {
                return (prDiff, $"GitHub PR #{_settings.GitHub.PullRequestNumber} ({_settings.GitHub.Owner}/{_settings.GitHub.Repo})");
            }
        }

        var autoLocalDiff = await TryGetLocalGitDiffAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(autoLocalDiff))
        {
            return (autoLocalDiff, $"Local git ({_settings.Review.GitDiffArguments})");
        }

        return (SampleDiff, "Built-in Sample Git Diff (fallback)");
    }

    private async Task<string?> TryGetLocalGitDiffAsync(CancellationToken cancellationToken)
    {
        var workingDir = string.IsNullOrWhiteSpace(_settings.Review.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : _settings.Review.WorkingDirectory;

        var args = string.IsNullOrWhiteSpace(_settings.Review.GitDiffArguments)
            ? "diff HEAD"
            : _settings.Review.GitDiffArguments;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = args,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                ? output
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryGetGitHubPrDiffAsync()
    {
        var gh = _settings.GitHub;
        if (string.IsNullOrWhiteSpace(gh.Token)
            || string.IsNullOrWhiteSpace(gh.Owner)
            || string.IsNullOrWhiteSpace(gh.Repo)
            || gh.PullRequestNumber <= 0)
        {
            return null;
        }

        try
        {
            var client = new GitHubClient(new ProductHeaderValue("CodeReviewAgents"))
            {
                Credentials = new Credentials(gh.Token)
            };

            var files = await client.PullRequest.Files(gh.Owner, gh.Repo, gh.PullRequestNumber);
            var sb = new StringBuilder();
            foreach (var file in files)
            {
                sb.AppendLine($"diff --git a/{file.FileName} b/{file.FileName}");
                sb.AppendLine($"--- a/{file.FileName}");
                sb.AppendLine($"+++ b/{file.FileName}");
                if (!string.IsNullOrWhiteSpace(file.Patch))
                {
                    sb.AppendLine(file.Patch);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitDiffProvider] GitHub PR diff alınamadı: {ex.Message}");
            return null;
        }
    }

    private const string SampleDiff = """
        diff --git a/src/Controllers/UserController.cs b/src/Controllers/UserController.cs
        index 83ab21f..c91e4a0 100644
        --- a/src/Controllers/UserController.cs
        +++ b/src/Controllers/UserController.cs
        @@ -12,11 +12,35 @@ namespace DemoApp.Controllers;
         public class UserController : ControllerBase
         {
             private readonly SqlConnection _connection;
        +    private const string AdminApiSecret = "sk-live-9876543210-hardcoded-secret";
         
             [HttpGet("search")]
        -    public async Task<IActionResult> SearchUsers(string username)
        +    public IActionResult SearchUsers(string username, string? apiKey)
             {
        -        var cmd = new SqlCommand("SELECT Id, Username FROM Users WHERE Username = @u", _connection);
        -        cmd.Parameters.AddWithValue("@u", username);
        -        var result = await cmd.ExecuteScalarAsync();
        -        return Ok(result);
        +        if (apiKey == AdminApiSecret)
        +        {
        +            Console.WriteLine("Admin search triggered with key: " + apiKey);
        +        }
        +
        +        var query = $"SELECT Id, Username, PasswordHash FROM Users WHERE Username LIKE '%{username}%'";
        +        var cmd = new SqlCommand(query, _connection);
        +        _connection.Open();
        +        var reader = cmd.ExecuteReader();
        +        var results = new List<object>();
        +        while (reader.Read())
        +        {
        +            results.Add(new { Id = reader[0], Username = reader[1], PasswordHash = reader[2] });
        +        }
        +        return Ok(results);
             }
        +
        +    [HttpPost("export")]
        +    public IActionResult ExportReport(string fileName)
        +    {
        +        var path = Path.Combine("/var/app/reports", fileName);
        +        var content = System.IO.File.ReadAllText(path);
        +        return Content(content);
        +    }
         }
        """;
}
