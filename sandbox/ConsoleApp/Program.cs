
using AlterNats;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Text;
using System.Threading.Tasks.Sources;
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
    ConnectOptions = ConnectOptions.Default with { Echo = true, Verbose = true }
});

await conn.ConnectAsync();



conn.Ping();




Console.ReadLine();
