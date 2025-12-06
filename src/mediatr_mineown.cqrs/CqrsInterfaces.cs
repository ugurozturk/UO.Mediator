using System.Threading;
using System.Threading.Tasks;

namespace MyCompanyName.MyProjectName.Cqrs;

// Command interfaces
public interface ICommand<TResult> { }

// Non-returning command marker
public interface ICommand { }

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

// Query interfaces
public interface IQuery<TResult> { }


public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

public interface IMediator
{
    Task SendCommandAsync(ICommand command, CancellationToken cancellationToken = default);

    Task<TResult> SendCommandAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<TResult> SendQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

}
