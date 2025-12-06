using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyCompanyName.MyProjectName.Cqrs;

public interface IPipelineBehavior<in TInput, TOutput>
{
    Task<TOutput> HandleAsync(TInput input, Func<Task<TOutput>> next, CancellationToken cancellationToken = default);
}
