using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UO.Mediator;
using UO.Mediator.Dispatching;
using UO.Mediator.Generated;
using UO.Mediator.NativeAotSample;

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<ExecutionTrace>();
services.AddNativeAotSampleUOMediator();

await using var provider = services.BuildServiceProvider(validateScopes: true);
var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

var result = await dispatcher.DispatchAsync(new AddRequest(20, 22));
await dispatcher.DispatchAsync(new RecordCommand("native-aot"));

var trace = provider.GetRequiredService<ExecutionTrace>();
if (result != 42 ||
    !trace.Events.SequenceEqual(["before", "handler", "after", "native-aot"]))
{
    throw new InvalidOperationException(
        $"Unexpected dispatch result. Result={result}; Trace={string.Join(',', trace.Events)}");
}

Console.WriteLine("NativeAOT generated dispatch passed: response, no-response, behavior.");

namespace UO.Mediator.NativeAotSample
{
    public sealed partial record AddRequest(int Left, int Right) : IRequest<int>;

    public sealed class AddRequestHandler(ExecutionTrace trace)
        : IRequestHandler<AddRequest, int>
    {
        public Task<int> HandleAsync(AddRequest request)
        {
            trace.Events.Add("handler");
            return Task.FromResult(request.Left + request.Right);
        }
    }

    public sealed class AddRequestBehavior(ExecutionTrace trace)
        : IRequestBehavior<AddRequest, int>
    {
        public async Task<int> HandleAsync(
            AddRequest request,
            RequestHandlerNext<AddRequest, int> next)
        {
            trace.Events.Add("before");
            var result = await next.InvokeAsync();
            trace.Events.Add("after");
            return result;
        }
    }

    public sealed partial record RecordCommand(string Value) : IRequest;

    public sealed class RecordCommandHandler(ExecutionTrace trace)
        : IRequestHandler<RecordCommand>
    {
        public Task HandleAsync(RecordCommand request)
        {
            trace.Events.Add(request.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class ExecutionTrace
    {
        public List<string> Events { get; } = [];
    }
}
