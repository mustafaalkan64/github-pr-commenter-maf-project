using CodeReviewAgents.Models;
using CodeReviewAgents.Services;
using CodeReviewAgents.Workflow.Executors;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow;

public static class CodeReviewWorkflowFactory
{
    public static Microsoft.Agents.AI.Workflows.Workflow Build(
        OpenRouterClientFactory agentFactory,
        GitHubPrCommentService gitHubService,
        SmtpEmailService emailService)
    {
        var securityAgent = agentFactory.CreateSecurityAgent();
        var codeQualityAgent = agentFactory.CreateCodeQualityAgent();
        var leadAgent = agentFactory.CreateLeadReviewerAgent();

        var diffDispatcher = new DiffDispatchExecutor();
        var securityReviewer = new SecurityReviewExecutor(securityAgent);
        var codeQualityReviewer = new CodeQualityReviewExecutor(codeQualityAgent);
        var reviewAggregator = new ReviewAggregatorExecutor();
        var leadReviewer = new LeadReviewExecutor(leadAgent);
        var prCommenter = new PrCommentExecutor(gitHubService);
        var emailNotifier = new EmailNotificationExecutor(emailService);
        var rejectedHandler = new ReviewRejectedExecutor();

        return new WorkflowBuilder(diffDispatcher)
            .WithName("ConcurrentCodeReviewWorkflow")
            .WithDescription("Paralel Security & Code Quality review ajanları -> Lead Agent denetim & onay -> GitHub PR yorumu -> SMTP E-posta")
            // 1. Fan-Out: Diff aynı anda hem Security hem Code Quality ajanına gönderilir (async concurrent)
            .AddFanOutEdge(diffDispatcher, [securityReviewer, codeQualityReviewer])
            // 2. Fan-In Barrier: Her iki ajan tamamlanana kadar bekler, sonra sonuçları ReviewAggregator'a aktarır
            .AddFanInBarrierEdge([securityReviewer, codeQualityReviewer], reviewAggregator)
            // 3. Lead Agent Denetim & Onay
            .AddEdge(reviewAggregator, leadReviewer)
            // 4. Koşullu yönlendirme: Yalnızca Lead Agent onaylarsa PR yorumu atılır -> ardından e-posta gönderilir
            .AddEdge<LeadReviewDecision>(leadReviewer, prCommenter, condition: decision => decision is { IsApproved: true })
            .AddEdge(prCommenter, emailNotifier)
            // 5. Red durumunda güvenli sonlandırma
            .AddEdge<LeadReviewDecision>(leadReviewer, rejectedHandler, condition: decision => decision is { IsApproved: false })
            .WithOutputFrom(emailNotifier, rejectedHandler)
            .Build();
    }
}
