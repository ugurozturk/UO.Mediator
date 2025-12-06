using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MyCompanyName.MyProjectName.Cqrs;

internal static class HandlerInvoker
{
        public static async Task<object?> InvokeVoidCommandAsync<TCommand>(ICommandHandler<TCommand> handler, object command, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        if (handler == null) throw new InvalidOperationException($"No handler registered for {typeof(TCommand).Name}");
        await handler.HandleAsync((TCommand)command!, cancellationToken);
        return null;
    }

    public static async Task<object?> InvokeAsync<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler, object command, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        if (handler == null) throw new InvalidOperationException($"No handler registered for {typeof(TCommand).Name}");
        var result = await handler.HandleAsync((TCommand)command!, cancellationToken);
        return (object?)result;
    }

    public static async Task<object?> InvokeQueryAsync<TQuery, TResult>(IQueryHandler<TQuery, TResult> handler, object query, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        if (handler == null) throw new InvalidOperationException($"No handler registered for {typeof(TQuery).Name}");
        var result = await handler.HandleAsync((TQuery)query!, cancellationToken);
        return (object?)result;
    }
}

internal static class TaskUtilities
{
    public static async Task<object?> BoxTaskResult<TResult>(Task<TResult> task)
    {
        var result = await task.ConfigureAwait(false);
        return (object?)result;
    }

    public static async Task<object?> BoxTaskVoid(Task task)
    {
        await task.ConfigureAwait(false);
        return null;
    }
}

public interface ICommandDispatcher
{
    Task<object?> DispatchAsync(object command, CancellationToken cancellationToken = default);
}

public class CommandDispatcher : ICommandDispatcher
{
    private readonly IServiceProvider _provider;
    private readonly ConcurrentDictionary<Type, Func<IServiceProvider, object, CancellationToken, Task<object?>>> _cache = new();

    public CommandDispatcher(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<object?> DispatchAsync(object command, CancellationToken cancellationToken = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        var commandType = command.GetType();
        var del = _cache.GetOrAdd(commandType, BuildDelegate);
        return del(_provider, command, cancellationToken);
    }

    private Func<IServiceProvider, object, CancellationToken, Task<object?>> BuildDelegate(Type commandType)
    {
        var iCommand = commandType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
        Type handlerInterface;
        Type resultType;

        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
        var cmdParam = Expression.Parameter(typeof(object), "cmd");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        Expression bodyExpression;

        if (iCommand != null)
        {
            // ICommand<TResult>
            resultType = iCommand.GetGenericArguments()[0];
            handlerInterface = typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType);
        }
        else
        {
            var iQuery = commandType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (iQuery != null)
            {
                // IQuery<TResult>
                resultType = iQuery.GetGenericArguments()[0];
                handlerInterface = typeof(IQueryHandler<,>).MakeGenericType(commandType, resultType);
            }
            else
            {
                // Non-generic command (ICommand)
                var nonGenericCommand = typeof(ICommand);

                if (commandType.GetInterfaces().Any(i => i == nonGenericCommand))
                {
                    handlerInterface = typeof(ICommandHandler<>).MakeGenericType(commandType);
                    resultType = null!;
                }
                else
                {
                    throw new InvalidOperationException($"The object of type {commandType.Name} does not implement ICommand<TResult>, IQuery<TResult>, or ICommand.");
                }
            }
        }

        var getServiceMethod = typeof(ServiceProviderServiceExtensions).GetMethod("GetService", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(handlerInterface);

        var getServiceCall = Expression.Call(getServiceMethod, spParam);
        var castHandler = Expression.Convert(getServiceCall, handlerInterface);

        // Build bodyExpression using the resolved handlerInterface and the castHandler
        if (iCommand != null)
        {
            var handleMethod = handlerInterface.GetMethod("HandleAsync")!; // Task<TResult> HandleAsync(TCommand, CancellationToken)
            var callHandle = Expression.Call(castHandler, handleMethod, Expression.Convert(cmdParam, commandType), ctParam);
            var boxMethod = typeof(TaskUtilities).GetMethod("BoxTaskResult")!.MakeGenericMethod(resultType);
            bodyExpression = Expression.Call(boxMethod, callHandle);
        }
        else
        {
            var iQuery = commandType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (iQuery != null)
            {
                var handleMethod = handlerInterface.GetMethod("HandleAsync")!; // Task<TResult> HandleAsync(TQuery, CancellationToken)
                var callHandle = Expression.Call(castHandler, handleMethod, Expression.Convert(cmdParam, commandType), ctParam);
                var boxMethod = typeof(TaskUtilities).GetMethod("BoxTaskResult")!.MakeGenericMethod(resultType);
                bodyExpression = Expression.Call(boxMethod, callHandle);
            }
            else
            {
                // Non-generic command
                var handleMethod = handlerInterface.GetMethod("HandleAsync")!; // Task HandleAsync(TCommand, CancellationToken)
                var callHandle = Expression.Call(castHandler, handleMethod, Expression.Convert(cmdParam, commandType), ctParam);
                var boxVoid = typeof(TaskUtilities).GetMethod("BoxTaskVoid")!;
                bodyExpression = Expression.Call(boxVoid, callHandle);
            }
        }

        var lambda = Expression.Lambda<Func<IServiceProvider, object, CancellationToken, Task<object?>>>(bodyExpression, spParam, cmdParam, ctParam);
        return lambda.Compile();
    }
}
