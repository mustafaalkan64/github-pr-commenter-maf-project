using CodeReviewAgents.Configuration;
using Octokit;

namespace CodeReviewAgents.Services;

public sealed class GitHubPrCommentService
{
    private const string CommentMarker = "<!-- code-review-agents:ai-pr-review -->";
    private readonly GitHubSettings _settings;

    public GitHubPrCommentService(GitHubSettings settings)
    {
        _settings = settings;
    }

    public async Task<(bool Posted, string? CommentUrl, string StatusMessage)> PostPullRequestCommentAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string markdownBody)
    {
        if (_settings.DryRun)
        {
            return (
                Posted: false,
                CommentUrl: null,
                StatusMessage: $"[DRY-RUN] GitHub PR #{pullRequestNumber} ({owner}/{repo}) yorumu simüle edildi (gerçek gönderim kapalı)."
            );
        }

        if (string.IsNullOrWhiteSpace(_settings.Token))
        {
            throw new InvalidOperationException("GitHub:Token tanımlı değil. PR yorumu atmak için GitHub Personal Access Token girin veya DryRun=true yapın.");
        }

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || pullRequestNumber <= 0)
        {
            throw new InvalidOperationException("GitHub Owner, Repo ve PullRequestNumber değerleri geçerli olmalıdır.");
        }

        var client = new GitHubClient(new ProductHeaderValue("CodeReviewAgents"))
        {
            Credentials = new Credentials(_settings.Token)
        };

        var bodyWithMarker = markdownBody.Contains(CommentMarker, StringComparison.Ordinal)
            ? markdownBody
            : $"{CommentMarker}\n{markdownBody}";

        // Reuse and update existing AI review comment on the same PR to keep conversation clean.
        var existingComments = await client.Issue.Comment.GetAllForIssue(owner, repo, pullRequestNumber);
        var existingBotComment = existingComments.LastOrDefault(c =>
            !string.IsNullOrEmpty(c.Body) && c.Body.Contains(CommentMarker, StringComparison.Ordinal));

        if (existingBotComment is not null)
        {
            var updated = await client.Issue.Comment.Update(owner, repo, existingBotComment.Id, bodyWithMarker);

            return (
                Posted: true,
                CommentUrl: updated.HtmlUrl,
                StatusMessage: $"GitHub PR #{pullRequestNumber} mevcut AI yorumu güncellendi: {updated.HtmlUrl}"
            );
        }

        // GitHub REST API treats PR conversation comments as Issue Comments on the PR number.
        var comment = await client.Issue.Comment.Create(owner, repo, pullRequestNumber, bodyWithMarker);

        return (
            Posted: true,
            CommentUrl: comment.HtmlUrl,
            StatusMessage: $"GitHub PR #{pullRequestNumber} yorumu başarıyla gönderildi: {comment.HtmlUrl}"
        );
    }
}
