using Uozturk.Mediator.Cqrs;

public class LoggingBehavior<TInput> : IPipelineBehavior<TInput>
{
    public async Task HandleAsync(TInput input, Func<Task> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[LoggingBehavior] Handling {typeof(TInput).Name}");
        await next();
        Console.WriteLine($"[LoggingBehavior] Handled {typeof(TInput).Name}");
    }
}

public class LoggingBehavior<TInput, TOutput> : IPipelineBehavior<TInput, TOutput>
{
    public async Task<TOutput> HandleAsync(TInput input, Func<Task<TOutput>> next, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[LoggingBehavior] Handling {typeof(TInput).Name}");
        var result = await next();
        Console.WriteLine($"[LoggingBehavior] Handled {typeof(TInput).Name}");
        return result;
    }
}
