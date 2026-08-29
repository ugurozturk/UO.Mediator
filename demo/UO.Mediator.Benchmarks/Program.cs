using BenchmarkDotNet.Running;
using UO.Mediator.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(MediatorDispatchBenchmarks).Assembly)
    .Run(args);
