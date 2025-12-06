using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace MyCompanyName.MyProjectName;

public class HelloWorldService : ITransientDependency
{
    public ILogger<HelloWorldService> Logger { get; set; }
    public IMediator Mediator { get; set; }

    public HelloWorldService(IMediator mediator)
    {
        Mediator = mediator;
        Logger = NullLogger<HelloWorldService>.Instance;
    }

    public async Task SayHelloAsync()
    {
        Logger.LogInformation("Hello World!");
        var result = await Mediator.SendCommandAsync(new PingCommand { Message = "Hello from HelloWorldService" });
        Console.WriteLine($"Mediator result: {result}");
        var serverTime = await Mediator.SendQueryAsync(new GetServerTimeQuery());
        Console.WriteLine($"Server time (UTC): {serverTime:O}");
    }
}