using CodeReviewAgents.Models;
using Microsoft.Agents.AI.Workflows;

namespace CodeReviewAgents.Workflow.Executors;

public sealed class DiffDispatchExecutor : Executor<CodeReviewRequest, CodeReviewRequest>
{
    public DiffDispatchExecutor()
        : base("DiffDispatcher")
    {
    }

    public override ValueTask<CodeReviewRequest> HandleAsync(
        CodeReviewRequest message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[DiffDispatcher] Git diff alındı ({message.SourceDescription}, {message.Diff.Length} karakter). Security ve Code Quality ajanlarına paralel dağıtılıyor...");
        return ValueTask.FromResult(message);
    }
}
