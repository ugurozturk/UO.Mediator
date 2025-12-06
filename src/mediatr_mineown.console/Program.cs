using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
services.AddSingleton<IMediator, Mediator>();

// Register sample handlers and behaviors
// Register services using ABP-like marker scanning
services.AddAbpStyleDependencies();

// register open-generic pipeline behavior (still explicit)
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

using var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

// Use typed-inferred overloads — compiler infers TResult from the concrete command/query types
var result = await mediator.SendCommandAsync(new PingCommand { Message = "Hello" });
Console.WriteLine($"Mediator result: {result}");

var serverTime = await mediator.SendQueryAsync(new GetServerTimeQuery());
Console.WriteLine($"Server time (UTC): {serverTime:O}");

// Keep console window open in debug scenarios
Console.WriteLine("Done. Press any key to exit...");
Console.ReadKey();
