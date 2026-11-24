using System.Diagnostics;
using CodeReviewAgents.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow.Executors;

public sealed class CodeQualityReviewExecutor : Executor<CodeReviewRequest, SpecialistReviewResult>
{
    private readonly AIAgent _codeQualityAgent;

    public CodeQualityReviewExecutor(AIAgent codeQualityAgent)
        : base("CodeQualityReviewAgent")
    {
        _codeQualityAgent = codeQualityAgent;
    }

    public override async ValueTask<SpecialistReviewResult> HandleAsync(
        CodeReviewRequest message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[CodeQualityReviewAgent] Paralel kod kalitesi incelemesi başladı (Thread #{Environment.CurrentManagedThreadId})...");
        var sw = Stopwatch.StartNew();

        var prompt = $"""
            Aşağıdaki git diff'inde `+` ile eklenen her satırı tek tek incele.
            Özellikle şu alanlardaki ihlalleri eksiksiz raporla:
            - Code smell, kullanılmayan/geçici/test amaçlı değişkenler, dead code ve adlandırma (naming) ihlalleri
            - Technical debt (kod tekrarı, magic string/number, uzun metot, hardcoded değerler, yüksek karmaşıklık)
            - SOLID prensipleri (SRP, OCP, LSP, ISP, DIP) ve Design Pattern iyileştirme fırsatları veya anti-pattern'ler
            - Async/await, kaynak yönetimi, hata yönetimi ve performans

            Kaynak: {message.SourceDescription}

            ```diff
            {message.Diff}
            ```
            """;

        var response = await _codeQualityAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        sw.Stop();

        var findings = response.Text ?? "Code Quality ajanından yanıt alınamadı.";
        Console.WriteLine($"[CodeQualityReviewAgent] İnceleme tamamlandı ({sw.Elapsed.TotalSeconds:F1}s).");

        return new SpecialistReviewResult(
            Request: message,
            AgentName: _codeQualityAgent.Name ?? "CodeQualityReviewerAgent",
            Category: "CodeQuality",
            FindingsMarkdown: findings,
            Duration: sw.Elapsed);
    }
}
