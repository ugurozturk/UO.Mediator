using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UO.Mediator;
using UO.Mediator.Dispatching;
using UO.Mediator.Generated;
using UO.Mediator.MultiAssembly.Contracts;

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<MultiAssemblyTrace>();
services.AddMultiAssemblyHandlersAUOMediator();
services.AddMultiAssemblyHandlersBUOMediator();

await using var provider = services.BuildServiceProvider(validateScopes: true);
provider.ValidateUOMediator();
var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

var result = await dispatcher.DispatchAsync(new CalculateRequest(21));
await dispatcher.DispatchAsync(new AuditCommand("handler-b"));

var trace = provider.GetRequiredService<MultiAssemblyTrace>();
if (result != 42 ||
    !trace.Events.SequenceEqual(
        ["behavior-a-before", "handler-a", "behavior-a-after", "handler-b"]))
{
    throw new InvalidOperationException(
        $"Unexpected multi-assembly result. Result={result}; " +
        $"Trace={string.Join(',', trace.Events)}");
}

Console.WriteLine("Multi-assembly generated dispatch passed: contracts + handlers A + handlers B.");
