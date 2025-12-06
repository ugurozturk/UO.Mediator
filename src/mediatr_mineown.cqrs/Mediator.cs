using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MyCompanyName.MyProjectName.Cqrs;

public class Mediator : IMediator
{
    private readonly IServiceProvider _provider;
    private readonly ICommandDispatcher _dispatcher;

    public Mediator(IServiceProvider provider, ICommandDispatcher dispatcher)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task SendCommandAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        await _dispatcher.DispatchAsync(command, cancellationToken);
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
