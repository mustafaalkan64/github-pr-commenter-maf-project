using CodeReviewAgents.Models;
using CodeReviewAgents.Services;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow.Executors;

public sealed class PrCommentExecutor : Executor<LeadReviewDecision, PrCommentResult>
{
    private readonly GitHubPrCommentService _gitHubService;

    public PrCommentExecutor(GitHubPrCommentService gitHubService)
        : base("GitHubPrCommenter")
    {
        _gitHubService = gitHubService;
    }

    public override async ValueTask<PrCommentResult> HandleAsync(
        LeadReviewDecision message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[GitHubPrCommenter] Lead ajan onayı alındı. GitHub PR yorumu gönderiliyor...");

        var (posted, commentUrl, statusMessage) = await _gitHubService.PostPullRequestCommentAsync(
            message.Request.RepositoryOwner,
            message.Request.RepositoryName,
            message.Request.PullRequestNumber,
            message.FinalPrCommentMarkdown);

        Console.WriteLine($"[GitHubPrCommenter] {statusMessage}");

        return new PrCommentResult(
            Decision: message,
            PostedToGitHub: posted,
            CommentUrl: commentUrl,
            StatusMessage: statusMessage);
    }
}
