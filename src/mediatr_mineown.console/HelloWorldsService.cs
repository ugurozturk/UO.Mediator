using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;
using MyCompanyName.MyProjectName.Cqrs;

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
        await Mediator.SendCommandAsync(new PingCommandNonMessage { Message = "Hello from PingCommandNonMessage" });
        var result = await Mediator.SendCommandAsync<PingCommand, string>(new PingCommand { Message = "Hello from PingCommand" });
        Console.WriteLine($"Mediator result: {result}");
        var serverTime = await Mediator.SendQueryAsync<GetServerTimeQuery, DateTime>(new GetServerTimeQuery());
        Console.WriteLine($"Server time (UTC): {serverTime:O}");
    }
}