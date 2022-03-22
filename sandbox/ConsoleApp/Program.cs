
using AlterNats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;

var provider = new ServiceCollection()
    .AddLogging(x =>
    {
        x.ClearProviders();
        x.SetMinimumLevel(LogLevel.Trace);
        x.AddZLoggerConsole();
    })
    .BuildServiceProvider();

var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

await using var conn = new NatsConnection(NatsOptions.Default with
{
    LoggerFactory = loggerFactory,
    ConnectOptions = ConnectOptions.Default with { Echo = true, Verbose = false }
});

await conn.ConnectAsync();



conn.Ping();

var d1 = conn.Subscribe<int>("foo.bar", x => Console.WriteLine($"Received1:{x}"));
var d2 = conn.Subscribe<int>("foo.bar", x => Console.WriteLine($"Received2:{x}"));
var d3 = conn.Subscribe<int>("foo.bar", x => Console.WriteLine($"Received3:{x}"));

d1.Dispose();





Console.ReadLine();

d2.Dispose();
d3.Dispose();

Console.ReadLine();
