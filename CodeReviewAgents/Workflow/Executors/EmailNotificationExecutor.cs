using CodeReviewAgents.Models;
using CodeReviewAgents.Services;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow.Executors;

public sealed class EmailNotificationExecutor : Executor<PrCommentResult, WorkflowFinalReport>
{
    private readonly SmtpEmailService _emailService;

    public EmailNotificationExecutor(SmtpEmailService emailService)
        : base("SmtpEmailNotifier")
    {
        _emailService = emailService;
    }

    public override async ValueTask<WorkflowFinalReport> HandleAsync(
        PrCommentResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[SmtpEmailNotifier] PR yorumu e-posta ile gönderiliyor...");

        var req = message.Decision.Request;
        var subject = req.PullRequestNumber > 0
            ? $"[AI Code Review] {req.RepositoryOwner}/{req.RepositoryName} PR #{req.PullRequestNumber} Yorumu"
            : $"[AI Code Review] {req.SourceDescription} - Lead Onaylı Rapor";

        var (sent, statusMessage) = await _emailService.SendPrReviewEmailAsync(
            subject,
            message.Decision.FinalPrCommentMarkdown,
            message.CommentUrl,
            cancellationToken);

        Console.WriteLine($"[SmtpEmailNotifier] {statusMessage}");

        return new WorkflowFinalReport(
            ApprovedByLead: true,
            PrCommentPosted: message.PostedToGitHub,
            PrCommentUrl: message.CommentUrl,
            EmailSent: sent,
            Summary: $"{message.StatusMessage} | {statusMessage}",
            FinalCommentMarkdown: message.Decision.FinalPrCommentMarkdown);
    }
}
