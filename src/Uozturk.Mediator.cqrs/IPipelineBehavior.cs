using System;
using System.Threading;
using System.Threading.Tasks;

namespace Uozturk.Mediator.Cqrs;

public interface IPipelineBehavior<in TInput>
{
    Task HandleAsync(TInput input, Func<Task> next, CancellationToken cancellationToken = default);
}

public interface IPipelineBehavior<in TInput, TOutput>
{
    Task<TOutput> HandleAsync(TInput input, Func<Task<TOutput>> next, CancellationToken cancellationToken = default);
}
