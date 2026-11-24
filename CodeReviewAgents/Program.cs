using CodeReviewAgents.Configuration;
using CodeReviewAgents.Models;
using CodeReviewAgents.Services;
using CodeReviewAgents.Workflow;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = new AppSettings();
configuration.Bind(settings);

// Support common flat environment variable overrides for convenience
settings.OpenRouter.ApiKey = FirstNonEmpty(
    Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"),
    settings.OpenRouter.ApiKey);

settings.OpenRouter.Model = FirstNonEmpty(
    Environment.GetEnvironmentVariable("OPENROUTER_MODEL"),
    settings.OpenRouter.Model);

settings.GitHub.Token = FirstNonEmpty(
    Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
    settings.GitHub.Token);

settings.Smtp.Password = FirstNonEmpty(
    Environment.GetEnvironmentVariable("SMTP_PASSWORD"),
    settings.Smtp.Password);

var graphOnly = args.Contains("--graph-only", StringComparer.OrdinalIgnoreCase);

if (graphOnly && string.IsNullOrWhiteSpace(settings.OpenRouter.ApiKey))
{
    settings.OpenRouter.ApiKey = "sk-or-graph-validation-placeholder";
}

Console.WriteLine("=== Microsoft Agent Framework (.NET 10) - Concurrent Code Review Workflow ===");
Console.WriteLine($"LLM Endpoint : {settings.OpenRouter.BaseUrl}");
Console.WriteLine($"LLM Model    : {settings.OpenRouter.Model}");
Console.WriteLine($"GitHub PR    : {(settings.GitHub.DryRun ? "DRY-RUN" : $"{settings.GitHub.Owner}/{settings.GitHub.Repo}#{settings.GitHub.PullRequestNumber}")}");
Console.WriteLine($"SMTP Email   : {(settings.Smtp.DryRun ? "DRY-RUN" : $"{settings.Smtp.Host}:{settings.Smtp.Port} -> {settings.Smtp.ToEmail}")}");
Console.WriteLine();

using var openRouterFactory = new OpenRouterClientFactory(settings.OpenRouter);
var gitHubService = new GitHubPrCommentService(settings.GitHub);
var emailService = new SmtpEmailService(settings.Smtp);

var workflow = CodeReviewWorkflowFactory.Build(openRouterFactory, gitHubService, emailService);

Console.WriteLine("--- Workflow Mermaid Diyagramı ---");
Console.WriteLine(workflow.ToMermaidString());
Console.WriteLine("----------------------------------\n");

if (graphOnly)
{
    Console.WriteLine("Workflow grafiği başarıyla doğrulandı (--graph-only).");
    return;
}

var diffProvider = new GitDiffProvider(settings);
var (diff, diffSource) = await diffProvider.GetDiffAsync();

var reviewRequest = new CodeReviewRequest(
    Diff: diff,
    SourceDescription: diffSource,
    RepositoryOwner: settings.GitHub.Owner,
    RepositoryName: settings.GitHub.Repo,
    PullRequestNumber: settings.GitHub.PullRequestNumber);

await using StreamingRun run = await InProcessExecution.Default.RunStreamingAsync(workflow, reviewRequest);

WorkflowFinalReport? finalReport = null;
var hasWorkflowError = false;

await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    switch (evt)
    {
        case ExecutorFailedEvent failed:
            hasWorkflowError = true;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[HATA] Executor '{failed.ExecutorId}' başarısız: {failed.Data}");
            Console.ResetColor();
            break;

        case WorkflowErrorEvent error:
            hasWorkflowError = true;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[WORKFLOW HATA] {error.Exception}");
            Console.ResetColor();
            break;

        case WorkflowOutputEvent output when output.Is<WorkflowFinalReport>(out var report):
            finalReport = report;
            break;
    }
}

Console.WriteLine("\n================ WORKFLOW SONUCU ================");
if (finalReport is null)
{
    Console.WriteLine("Workflow çıktı üretmeden sonlandı.");
    Environment.ExitCode = 1;
    return;
}

if (hasWorkflowError)
{
    Environment.ExitCode = 1;
}

Console.WriteLine($"Lead Onayı     : {(finalReport.ApprovedByLead ? "EVET" : "HAYIR")}");
Console.WriteLine($"PR Yorumu      : {(finalReport.PrCommentPosted ? $"GÖNDERİLDİ ({finalReport.PrCommentUrl})" : "Gönderilmedi / Dry-Run")}");
Console.WriteLine($"E-posta        : {(finalReport.EmailSent ? "GÖNDERİLDİ" : "Gönderilmedi / Dry-Run")}");
Console.WriteLine($"Özet           : {finalReport.Summary}");
Console.WriteLine("\n--- Nihai PR Yorumu (Markdown) ---\n");
Console.WriteLine(finalReport.FinalCommentMarkdown);

static string FirstNonEmpty(params string?[] values)
{
    foreach (var v in values)
    {
        if (!string.IsNullOrWhiteSpace(v))
        {
            return v;
        }
    }
    return string.Empty;
}

public partial class Program;
