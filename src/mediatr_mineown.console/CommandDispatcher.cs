using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

internal static class HandlerInvoker
{
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
        // Support both ICommand<TResult> and IQuery<TResult>
        var iCommand = commandType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
        MethodInfo invokerMethod;
        Type handlerInterface;
        Type resultType;

        if (iCommand != null)
        {
            resultType = iCommand.GetGenericArguments()[0];
            handlerInterface = typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType);
            invokerMethod = typeof(HandlerInvoker).GetMethod(nameof(HandlerInvoker.InvokeAsync), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(commandType, resultType);
        }
        else
        {
            var iQuery = commandType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (iQuery == null)
                throw new InvalidOperationException($"The object of type {commandType.Name} does not implement ICommand<TResult> or IQuery<TResult>.");

            resultType = iQuery.GetGenericArguments()[0];
            handlerInterface = typeof(IQueryHandler<,>).MakeGenericType(commandType, resultType);
            invokerMethod = typeof(HandlerInvoker).GetMethod(nameof(HandlerInvoker.InvokeQueryAsync), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(commandType, resultType);
        }

        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
        var cmdParam = Expression.Parameter(typeof(object), "cmd");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var getServiceMethod = typeof(ServiceProviderServiceExtensions).GetMethod("GetService", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(handlerInterface);

        var getServiceCall = Expression.Call(getServiceMethod, spParam);
        var castHandler = Expression.Convert(getServiceCall, handlerInterface);

        var callInvoker = Expression.Call(invokerMethod, castHandler, cmdParam, ctParam);

        var lambda = Expression.Lambda<Func<IServiceProvider, object, CancellationToken, Task<object?>>>(callInvoker, spParam, cmdParam, ctParam);
        return lambda.Compile();
    }
}
