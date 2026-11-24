using System.Diagnostics;
using CodeReviewAgents.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow.Executors;

public sealed class SecurityReviewExecutor : Executor<CodeReviewRequest, SpecialistReviewResult>
{
    private readonly AIAgent _securityAgent;

    public SecurityReviewExecutor(AIAgent securityAgent)
        : base("SecurityReviewAgent")
    {
        _securityAgent = securityAgent;
    }

    public override async ValueTask<SpecialistReviewResult> HandleAsync(
        CodeReviewRequest message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[SecurityReviewAgent] Paralel güvenlik incelemesi başladı (Thread #{Environment.CurrentManagedThreadId})...");
        var sw = Stopwatch.StartNew();

        var prompt = $"""
            Aşağıdaki git diff'ini güvenlik açısından incele.
            Kaynak: {message.SourceDescription}

            ```diff
            {message.Diff}
            ```
            """;

        var response = await _securityAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        sw.Stop();

        var findings = response.Text ?? "Güvenlik ajanından yanıt alınamadı.";
        Console.WriteLine($"[SecurityReviewAgent] İnceleme tamamlandı ({sw.Elapsed.TotalSeconds:F1}s).");

        return new SpecialistReviewResult(
            Request: message,
            AgentName: _securityAgent.Name ?? "SecurityReviewerAgent",
            Category: "Security",
            FindingsMarkdown: findings,
            Duration: sw.Elapsed);
    }
}
