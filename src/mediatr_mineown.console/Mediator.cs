using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

public class Mediator : IMediator
{
    private readonly IServiceProvider _provider;
    private readonly ICommandDispatcher _dispatcher;

    public Mediator(IServiceProvider provider, ICommandDispatcher dispatcher)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<TResult> SendCommandAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult>
    {
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

    public async Task<object?> SendCommandAsync(object command, CancellationToken cancellationToken = default)
    {
        return await _dispatcher.DispatchAsync(command, cancellationToken);
    }

    public async Task<object?> SendQueryAsync(object query, CancellationToken cancellationToken = default)
    {
        return await _dispatcher.DispatchAsync(query, cancellationToken);
    }

    public async Task<TResult> SendCommandAsync<TResult>(object command, CancellationToken cancellationToken = default)
    {
        var obj = await _dispatcher.DispatchAsync(command, cancellationToken);
        return (TResult)obj!;
    }

    public async Task<TResult> SendQueryAsync<TResult>(object query, CancellationToken cancellationToken = default)
    {
        var obj = await _dispatcher.DispatchAsync(query, cancellationToken);
        return (TResult)obj!;
    }

    public async Task<TResult> SendCommandAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        var obj = await _dispatcher.DispatchAsync(command, cancellationToken);
        return (TResult)obj!;
    }

    public async Task<TResult> SendQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        var obj = await _dispatcher.DispatchAsync(query, cancellationToken);
        return (TResult)obj!;
    }
}