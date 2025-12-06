using Microsoft.Extensions.DependencyInjection;

namespace MyCompanyName.MyProjectName.Cqrs;

public class Mediator : IMediator
{
    private readonly IServiceProvider _provider;

    public Mediator(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task SendCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        var handler = _provider.GetService<ICommandHandler<TCommand>>();
        if (handler == null) throw new InvalidOperationException($"No handler registered for {typeof(TCommand).Name}");

        var behaviors = _provider.GetServices<IPipelineBehavior<TCommand>>().Reverse();

        Func<Task> handlerDelegate = () => handler.HandleAsync(command, cancellationToken);
        foreach (var behavior in behaviors)
        {
            var next = handlerDelegate;
            handlerDelegate = () => behavior.HandleAsync(command, next, cancellationToken);
        }

        await handlerDelegate();
    }

    public async Task<TResult> SendCommandAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult>
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        var handler = _provider.GetService<ICommandHandler<TCommand, TResult>>();
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler registered for {typeof(TCommand).Name}");
        }

        var behaviors = _provider.GetServices<IPipelineBehavior<TCommand, TResult>>().Reverse();

        Func<Task<TResult>> handlerDelegate = () => handler.HandleAsync(command, cancellationToken);
        foreach (var behavior in behaviors)
        {
            var next = handlerDelegate;
            handlerDelegate = () => behavior.HandleAsync(command, next, cancellationToken);
        }

        return await handlerDelegate();
    }

    public async Task<TResult> SendQueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default) where TQuery : IQuery<TResult>
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        var handler = _provider.GetService<IQueryHandler<TQuery, TResult>>();
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler registered for {typeof(TQuery).Name}");
        }

        var behaviors = _provider.GetServices<IPipelineBehavior<TQuery, TResult>>().Reverse();
        Func<Task<TResult>> handlerDelegate = () => handler.HandleAsync(query, cancellationToken);
        foreach (var behavior in behaviors)
        {
            var next = handlerDelegate;
            handlerDelegate = () => behavior.HandleAsync(query, next, cancellationToken);
        }

        return await handlerDelegate();
    }
}
