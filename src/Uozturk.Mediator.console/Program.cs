using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Volo.Abp;
using Uozturk.Mediator.Cqrs;

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

            builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            builder.Services.AddTransient(typeof(IPipelineBehavior<>), typeof(LoggingBehavior<>));

            // Automatically register handlers and pipeline behaviors for this mediator
            // Scan the current assembly (handlers are in the same assembly)
            builder.Services.AddCustomMediatorDependencies(typeof(Program).Assembly);

            // Register core mediator service
            builder.Services.AddSingleton<IMediator, Mediator>();

            var host = builder.Build();

            await host.InitializeAsync();


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