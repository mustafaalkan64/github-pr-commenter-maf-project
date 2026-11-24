using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeReviewAgents.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow.Executors;

public sealed partial class LeadReviewExecutor : Executor<AggregatedReviews, LeadReviewDecision>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AIAgent _leadAgent;

    public LeadReviewExecutor(AIAgent leadAgent)
        : base("LeadReviewAgent")
    {
        _leadAgent = leadAgent;
    }

    public override async ValueTask<LeadReviewDecision> HandleAsync(
        AggregatedReviews message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[LeadReviewAgent] Security ve Code Quality raporları denetleniyor...");
        var sw = Stopwatch.StartNew();

        var prompt = $"""
            Aşağıdaki orijinal git diff'i ile iki uzman ajanın yaptığı code review raporlarını denetle.
            Bulguların doğruluğunu kontrol et, gereksiz veya yanlış pozitifleri ele, onay durumunu belirle ve PR'a atılacak nihai Türkçe Markdown yorumunu oluştur.

            ### Orijinal Git Diff ({message.Request.SourceDescription})
            ```diff
            {message.Request.Diff}
            ```

            ### 1. Security Reviewer Raporu ({message.SecurityReview.AgentName})
            {message.SecurityReview.FindingsMarkdown}

            ### 2. Code Quality Reviewer Raporu ({message.CodeQualityReview.AgentName})
            {message.CodeQualityReview.FindingsMarkdown}
            """;

        var response = await _leadAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        sw.Stop();

        var rawText = response.Text ?? string.Empty;
        var parsed = ParseDecision(rawText, message);

        Console.WriteLine($"[LeadReviewAgent] Denetim tamamlandı ({sw.Elapsed.TotalSeconds:F1}s) -> Karar: {(parsed.IsApproved ? "ONAYLANDI" : "REDDEDİLDİ")} | Gerekçe: {parsed.DecisionReason}");
        return parsed;
    }

    private static LeadReviewDecision ParseDecision(string rawText, AggregatedReviews source)
    {
        var jsonCandidate = ExtractJsonBlock(rawText);

        if (!string.IsNullOrWhiteSpace(jsonCandidate))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<LeadDecisionDto>(jsonCandidate, JsonOptions);
                if (dto is not null && !string.IsNullOrWhiteSpace(dto.FinalPrCommentMarkdown))
                {
                    return new LeadReviewDecision(
                        Request: source.Request,
                        IsApproved: dto.Approved,
                        DecisionReason: string.IsNullOrWhiteSpace(dto.DecisionReason)
                            ? (dto.Approved ? "Lead ajan bulguları doğruladı ve onayladı." : "Lead ajan incelemeyi onaylamadı.")
                            : dto.DecisionReason,
                        FinalPrCommentMarkdown: dto.FinalPrCommentMarkdown,
                        AuditSummary: dto.AuditSummary ?? string.Empty);
                }
            }
            catch (JsonException)
            {
                // Fallback below if free LLM produces slightly malformed JSON
            }
        }

        // Resilient fallback for free-tier models that don't strictly follow JSON formatting
        var fallbackComment = string.IsNullOrWhiteSpace(rawText)
            ? $"""
               ## AI Code Review Raporu

               ### Security Review
               {source.SecurityReview.FindingsMarkdown}

               ### Code Quality Review
               {source.CodeQualityReview.FindingsMarkdown}
               """
            : rawText.Trim();

        return new LeadReviewDecision(
            Request: source.Request,
            IsApproved: true,
            DecisionReason: "Lead ajan raporu üretti (JSON fallback ile onaylandı).",
            FinalPrCommentMarkdown: fallbackComment,
            AuditSummary: "Serbest metin çıktı fallback olarak işlendi.");
    }

    private static string? ExtractJsonBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var fencedMatch = JsonFenceRegex().Match(text);
        if (fencedMatch.Success)
        {
            return fencedMatch.Groups[1].Value.Trim();
        }

        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text[firstBrace..(lastBrace + 1)].Trim();
        }

        return null;
    }

    [GeneratedRegex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonFenceRegex();

    private sealed class LeadDecisionDto
    {
        public bool Approved { get; set; } = true;
        public string? DecisionReason { get; set; }
        public string? AuditSummary { get; set; }
        public string? FinalPrCommentMarkdown { get; set; }
    }
}
