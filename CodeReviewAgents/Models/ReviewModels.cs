namespace CodeReviewAgents.Models;

public sealed record CodeReviewRequest(
    string Diff,
    string SourceDescription,
    string RepositoryOwner,
    string RepositoryName,
    int PullRequestNumber);

public sealed record SpecialistReviewResult(
    CodeReviewRequest Request,
    string AgentName,
    string Category,
    string FindingsMarkdown,
    TimeSpan Duration);

public sealed record AggregatedReviews(
    CodeReviewRequest Request,
    SpecialistReviewResult SecurityReview,
    SpecialistReviewResult CodeQualityReview);

public sealed record LeadReviewDecision(
    CodeReviewRequest Request,
    bool IsApproved,
    string DecisionReason,
    string FinalPrCommentMarkdown,
    string AuditSummary);

public sealed record PrCommentResult(
    LeadReviewDecision Decision,
    bool PostedToGitHub,
    string? CommentUrl,
    string StatusMessage);

public sealed record WorkflowFinalReport(
    bool ApprovedByLead,
    bool PrCommentPosted,
    string? PrCommentUrl,
    bool EmailSent,
    string Summary,
    string FinalCommentMarkdown);
