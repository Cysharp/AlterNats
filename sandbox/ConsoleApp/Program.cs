using AlterNats;
using AlterNats.Commands;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Text;
using ZLogger;

[MessagePackObject]
public class FooRequest
{

}

[MessagePackObject]
public class FooResponse
{
}

public class Program
{
    public static async Task Main()
    {
        var provider = new ServiceCollection()
            .AddLogging(x =>
            {
                x.ClearProviders();
                x.SetMinimumLevel(LogLevel.Trace);
                x.AddZLoggerConsole();
            })
            .BuildServiceProvider();

        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

        var options = NatsOptions.Default with
        {
            LoggerFactory = loggerFactory,
            Serializer = new MessagePackNatsSerializer(),
            ConnectOptions = ConnectOptions.Default with { Echo = true, Verbose = false }
        };

        var connection = new NatsConnection(options);
        await connection.ConnectAsync();


        // Server
        await connection.SubscribeRequestAsync<FooRequest, FooResponse>("hogemoge.key", req =>
        {
            Console.WriteLine("YEAH?");
            return new FooResponse();
        });

        // Client
        var response = await connection.RequestAsync<FooRequest, FooResponse>("hogemoge.key", new FooRequest());



        Console.ReadLine();

        var response2 = await connection.RequestAsync<FooRequest, FooResponse>("hogemoge.key", new FooRequest());

        Console.ReadLine();

    }

    //static void CalcCommandPushPop(NatsKey key, INatsSerializer serializer)
    //{
    //    var p = PublishCommand<int>.Create(key, 10, serializer);
    //    (p as ICommand).Return();

    //    JetBrains.Profiler.Api.MemoryProfiler.ForceGc();
    //    JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);
    //    JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Before");
    //    for (int i = 0; i < 1000; i++)
    //    {
    //        p = PublishCommand<int>.Create(key, 10, serializer);
    //        (p as ICommand).Return();
    //    }
    //    JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("After");
    //}


    //static void CalcSubscribe(NatsKey key, NatsConnection connection)
    //{
    //    var i = 0;
    //    var subscription = connection.Subscribe(key, (MyVector3 x) =>
    //    {
    //        i++;

    //        if (i == 2000)
    //        {
    //            JetBrains.Profiler.Api.MemoryProfiler.ForceGc();
    //            JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);
    //            JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Before");
    //        }
    //        else if (i == 3000)
    //        {
    //            JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("After");
    //            Console.WriteLine("END SNAP");
    //        }
    //    });

    //}


    //static async ValueTask CalcPublishAsync(NatsKey key, NatsConnection connection)
    //{
    //    JetBrains.Profiler.Api.MemoryProfiler.ForceGc();
    //    JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);
    //    JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Before Publish");
    //    await connection.PublishAsync(key, "foobar").ConfigureAwait(false);
    //    JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("After Publish");
    //}
}

[MessagePackObject]
public struct MyVector3
{
    [Key(0)]
    public float X;
    [Key(1)]
    public float Y;
    [Key(2)]
    public float Z;
}

