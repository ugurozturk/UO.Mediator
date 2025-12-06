using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

public class PingCommand : ICommand<string>
{
    public required string Message { get; set; }
}

public class PingHandler : ICommandHandler<PingCommand, string>
{
    public Task<string> HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"Pong: {command.Message}");
    }
}

// Query example: returns current server time
public class GetServerTimeQuery : IQuery<DateTime>
{
}

public class GetServerTimeHandler : IQueryHandler<GetServerTimeQuery, DateTime>, ITransientDependency
{
    public Task<DateTime> HandleAsync(GetServerTimeQuery query, CancellationToken cancellationToken = default)
    {
        // return current UTC time as dummy data
        return Task.FromResult(DateTime.UtcNow);
    }
}

