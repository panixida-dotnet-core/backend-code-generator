using PANiXiDA.Core.Backend.CodeGenerator.Configuration;
using PANiXiDA.Core.Backend.CodeGenerator.FileSystem;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation;

internal static class CodeGenerator
{
    internal static async Task<IReadOnlyList<OutputChange>> RunJobsAsync(
        RepositoryLayout repository,
        IReadOnlyList<JobSettings> jobs,
        CancellationToken cancellationToken)
    {
        using var plan = new GenerationPlan(repository);
        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await plan.AddJobAsync(job, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var changes = plan.Changes;
        OutputChange.ApplyAll(changes);
        return changes;
    }
}
