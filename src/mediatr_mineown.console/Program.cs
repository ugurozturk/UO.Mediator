// using Microsoft.Extensions.DependencyInjection;

// var services = new ServiceCollection();

// services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
// services.AddSingleton<IMediator, Mediator>();

// // Register sample handlers and behaviors
// // Register services using ABP-like marker scanning
// services.AddAbpStyleDependencies();

// // register open-generic pipeline behavior (still explicit)
// services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// using var provider = services.BuildServiceProvider();
// var mediator = provider.GetRequiredService<IMediator>();

// // Use typed-inferred overloads — compiler infers TResult from the concrete command/query types
// var result = await mediator.SendCommandAsync(new PingCommand { Message = "Hello" });
// Console.WriteLine($"Mediator result: {result}");

// var serverTime = await mediator.SendQueryAsync(new GetServerTimeQuery());
// Console.WriteLine($"Server time (UTC): {serverTime:O}");

// // Keep console window open in debug scenarios
// Console.WriteLine("Done. Press any key to exit...");
// Console.ReadKey();


using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Volo.Abp;

namespace MyCompanyName.MyProjectName;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        try
        {
            Log.Information("Starting console host.");

            var builder = Host.CreateApplicationBuilder(args);

            builder.Configuration.AddAppSettingsSecretsJson();
            builder.Logging.ClearProviders().AddSerilog();

            builder.ConfigureContainer(builder.Services.AddAutofacServiceProviderFactory());

            builder.Services.AddHostedService<MyProjectNameHostedService>();

            await builder.Services.AddApplicationAsync<MyProjectNameModule>();

            var host = builder.Build();

            await host.InitializeAsync();

            builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

            await host.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}