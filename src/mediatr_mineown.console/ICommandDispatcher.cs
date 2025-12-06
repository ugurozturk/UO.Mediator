using Volo.Abp.DependencyInjection;

public interface ICommandDispatcher : ITransientDependency
{
    Task<object?> DispatchAsync(object command, CancellationToken cancellationToken = default);
}