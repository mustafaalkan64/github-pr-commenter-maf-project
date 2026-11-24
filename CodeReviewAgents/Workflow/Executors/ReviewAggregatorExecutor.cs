using CodeReviewAgents.Models;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow.Executors;

public sealed class ReviewAggregatorExecutor : Executor<SpecialistReviewResult>
{
    private readonly Lock _syncLock = new();
    private readonly Dictionary<string, SpecialistReviewResult> _collected = new(StringComparer.OrdinalIgnoreCase);

    public ReviewAggregatorExecutor()
        : base("ReviewAggregator")
    {
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        return base.ConfigureProtocol(protocolBuilder)
            .SendsMessage<AggregatedReviews>();
    }

    public override async ValueTask HandleAsync(
        SpecialistReviewResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        AggregatedReviews? readyPayload = null;

        lock (_syncLock)
        {
            _collected[message.Category] = message;

            if (_collected.TryGetValue("Security", out var securityReview)
                && _collected.TryGetValue("CodeQuality", out var codeQualityReview))
            {
                readyPayload = new AggregatedReviews(
                    Request: message.Request,
                    SecurityReview: securityReview,
                    CodeQualityReview: codeQualityReview);

                _collected.Clear();
            }
        }

        if (readyPayload is not null)
        {
            Console.WriteLine($"[ReviewAggregator] Her iki paralel ajan tamamlandı (Security: {readyPayload.SecurityReview.Duration.TotalSeconds:F1}s, CodeQuality: {readyPayload.CodeQualityReview.Duration.TotalSeconds:F1}s). Lead ajana iletiliyor...");
            await context.SendMessageAsync(readyPayload, cancellationToken: cancellationToken);
        }
    }
}
