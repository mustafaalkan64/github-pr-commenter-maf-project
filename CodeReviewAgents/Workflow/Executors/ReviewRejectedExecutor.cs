using CodeReviewAgents.Models;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow.Executors;

public sealed class ReviewRejectedExecutor : Executor<LeadReviewDecision, WorkflowFinalReport>
{
    public ReviewRejectedExecutor()
        : base("ReviewRejectedHandler")
    {
    }

    public override ValueTask<WorkflowFinalReport> HandleAsync(
        LeadReviewDecision message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[ReviewRejectedHandler] Lead ajan incelemeyi onaylamadığı için PR yorumu ve e-posta atlandı. Gerekçe: {message.DecisionReason}");

        var report = new WorkflowFinalReport(
            ApprovedByLead: false,
            PrCommentPosted: false,
            PrCommentUrl: null,
            EmailSent: false,
            Summary: $"Lead ajan onaylamadı: {message.DecisionReason}",
            FinalCommentMarkdown: message.FinalPrCommentMarkdown);

        return ValueTask.FromResult(report);
    }
}
