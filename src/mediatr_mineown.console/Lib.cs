// Command interfaces
public interface ICommand<TResult> { }

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

// Query interfaces
public interface IQuery<TResult> { }

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

public interface IMediator
{
    Task<TResult> SendCommandAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<TResult> SendQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}