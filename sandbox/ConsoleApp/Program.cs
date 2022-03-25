using AlterNats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using ZLogger;

var provider = new ServiceCollection()
    .AddLogging(x =>
    {
        x.ClearProviders();
        //x.SetMinimumLevel(LogLevel.Information);
        //x.AddZLoggerConsole();
    })
    .BuildServiceProvider();

var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

await using var connection = new NatsConnection(NatsOptions.Default with
{
    // LoggerFactory = loggerFactory,
    ConnectOptions = ConnectOptions.Default with { Echo = true, Verbose = false }
});

await connection.ConnectAsync();

var key = new NatsKey("foobar");

for (int i = 0; i < 100; i++)
{
    await connection.PublishAsync(key, "foobar").ConfigureAwait(false); // cache???
}

await CalcPublishAsync(key, connection).ConfigureAwait(false);

Console.WriteLine("END");




static async ValueTask CalcPublishAsync(NatsKey key, NatsConnection connection)
{
    JetBrains.Profiler.Api.MemoryProfiler.ForceGc();
    JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);
    JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Before Publish");
    await connection.PublishAsync(key, "foobar").ConfigureAwait(false);
    JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("After Publish");
}



public struct MyVector3
{
    public float X;
    public float Y;
    public float Z;
}
