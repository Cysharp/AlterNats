using AlterNats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
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

await using var connection = new NatsConnection(NatsOptions.Default with
{
    LoggerFactory = loggerFactory,
    ConnectOptions = ConnectOptions.Default with { Echo = true, Verbose = false }
});

await connection.ConnectAsync();


var key = new NatsKey("foobar");

await connection.SubscribeAsync<byte[]>(key.Key, x =>
{
    global::System.Console.WriteLine("received:" + Encoding.UTF8.GetString(x));
});







await connection.SubscribeRequestAsync<string, int>("key", (req) =>
{


    return 1000;
});

var value = await connection.RequestAsync<string, int>("key", "foobarbaz");





connection.Publish("foobar", "foooon", Encoding.UTF8.GetBytes("tako"));


Console.ReadLine();






//static async ValueTask CalcPublishAsync(NatsKey key, NatsConnection connection)
//{
//    JetBrains.Profiler.Api.MemoryProfiler.ForceGc();
//    JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);
//    JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Before Publish");
//    await connection.PublishAsync(key, "foobar").ConfigureAwait(false);
//    JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("After Publish");
//}



public struct MyVector3
{
    public float X;
    public float Y;
    public float Z;
}
