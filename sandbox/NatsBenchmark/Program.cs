using AlterNats;
using AlterNats.Commands;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client;
using NatsBenchmark;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ZLogger;

var isPortableThreadPool = await IsRunOnPortableThreadPoolAsync();
Console.WriteLine($"RunOnPortableThreadPool:{isPortableThreadPool}");

var key = new NatsKey("foo");
var serializer = new MessagePackNatsSerializer();
var p = PublishCommand<Vector3>.Create(key, new Vector3(), serializer);
(p as ICommand).Return();

try
{
    // use only pubsub suite
    new Benchmark(args);
}
catch (Exception e)
{
    Console.WriteLine("Error: " + e.Message);
    Console.WriteLine(e);
}


// COMPlus_ThreadPool_UsePortableThreadPool=0 -> false
static Task<bool> IsRunOnPortableThreadPoolAsync()
{
    var tcs = new TaskCompletionSource<bool>();
    ThreadPool.QueueUserWorkItem(_ =>
    {
        var st = new StackTrace().ToString();
        tcs.TrySetResult(st.Contains("PortableThreadPool"));
    });
    return tcs.Task;
}


namespace NatsBenchmark
{
    partial class Benchmark
    {
        void RunPubSubAlterNats(string testName, long testCount, long testSize)
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
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOptions.Default with
            {
                // LoggerFactory = loggerFactory,
                UseThreadPoolCallback = false,
                ConnectOptions = ConnectOptions.Default with { Echo = false, Verbose = false }
            };

            object pubSubLock = new object();
            bool finished = false;
            int subCount = 0;

            byte[] payload = generatePayload(testSize);

            var pubConn = new AlterNats.NatsConnection(options);
            var subConn = new AlterNats.NatsConnection(options);

            pubConn.ConnectAsync().AsTask().Wait();
            subConn.ConnectAsync().AsTask().Wait();

            var key = new NatsKey(subject);

            // TODO:Async Subscribe
            var d = subConn.SubscribeAsync<byte[]>(subject, _ =>
           {
               Interlocked.Increment(ref subCount);
               // logger.LogInformation("here:{0}", subCount);

               if (subCount == testCount)
               {
                   lock (pubSubLock)
                   {
                       finished = true;
                       Monitor.Pulse(pubSubLock);
                   }
               }
           }).AsTask().Result;

            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < testCount; i++)
            {
                pubConn.Publish(key, payload);
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);

            }
            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }

        void RunPubSubAlterNatsVector3(string testName, long testCount)
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
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOptions.Default with
            {
                LoggerFactory = loggerFactory,
                Serializer = new MessagePackNatsSerializer(),
                UseThreadPoolCallback = false,
                ConnectOptions = ConnectOptions.Default with { Echo = false, Verbose = false }
            };

            object pubSubLock = new object();
            bool finished = false;
            int subCount = 0;

            // byte[] payload = generatePayload(testSize);

            var pubConn = new AlterNats.NatsConnection(options);
            var subConn = new AlterNats.NatsConnection(options);

            var key = new NatsKey(subject);

            pubConn.ConnectAsync().AsTask().Wait();
            subConn.ConnectAsync().AsTask().Wait();

            // TODO:Async Subscribe
            var d = subConn.SubscribeAsync<Vector3>(key.Key, _ =>
            {
                Interlocked.Increment(ref subCount);
                // logger.LogInformation("here:{0}", subCount);

                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("After");
                        Monitor.Pulse(pubSubLock);
                    }
                }
            }).AsTask().Result;

            MessagePackSerializer.Serialize(new Vector3());

            Stopwatch sw = Stopwatch.StartNew();

            JetBrains.Profiler.Api.MemoryProfiler.ForceGc();
            JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);
            JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot("Before");


            for (int i = 0; i < testCount; i++)
            {
                pubConn.Publish(key, new Vector3());
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);

            }
            sw.Stop();

            PrintResults(testName, sw, testCount, 16);

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }

        void runPubSubVector3(string testName, long testCount)
        {
            object pubSubLock = new object();
            bool finished = false;
            int subCount = 0;

            // byte[] payload = generatePayload(testSize);

            ConnectionFactory cf = new ConnectionFactory();

            Options o = ConnectionFactory.GetDefaultOptions();
            o.ClosedEventHandler = (_, __) => { };
            o.DisconnectedEventHandler = (_, __) => { };

            o.Url = url;
            o.SubChannelLength = 10000000;
            if (creds != null)
            {
                o.SetUserCredentials(creds);
            }
            o.AsyncErrorEventHandler += (sender, obj) =>
            {
                Console.WriteLine("Error: " + obj.Error);
            };

            IConnection subConn = cf.CreateConnection(o);
            IConnection pubConn = cf.CreateConnection(o);

            IAsyncSubscription s = subConn.SubscribeAsync(subject, (sender, args) =>
            {
                MessagePackSerializer.Deserialize<Vector3>(args.Message.Data);

                subCount++;
                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });
            s.SetPendingLimits(10000000, 1000000000);
            subConn.Flush();

            MessagePackSerializer.Serialize(new Vector3());

            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < testCount; i++)
            {
                pubConn.Publish(subject, MessagePackSerializer.Serialize(new Vector3()));
            }

            pubConn.Flush();

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);

            }
            sw.Stop();

            PrintResults(testName, sw, testCount, 16);

            pubConn.Close();
            subConn.Close();
        }

        void RunPubSubRedis(string testName, long testCount, long testSize)
        {

            var provider = new ServiceCollection()
                .AddLogging(x =>
                {
                    x.ClearProviders();
                    x.SetMinimumLevel(LogLevel.Information);
                    x.AddZLoggerConsole();
                })
                .BuildServiceProvider();


            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();

            object pubSubLock = new object();
            bool finished = false;
            int subCount = 0;

            byte[] payload = generatePayload(testSize);

            var pubConn = StackExchange.Redis.ConnectionMultiplexer.Connect("localhost");
            var subConn = StackExchange.Redis.ConnectionMultiplexer.Connect("localhost");


            subConn.GetSubscriber().Subscribe(subject, (channel, v) =>
            {
                Interlocked.Increment(ref subCount);
                //logger.LogInformation("here?:" + subCount);

                if (subCount == testCount)
                {
                    lock (pubSubLock)
                    {
                        finished = true;
                        Monitor.Pulse(pubSubLock);
                    }
                }
            });

            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < testCount; i++)
            {
                _ = pubConn.GetDatabase().PublishAsync(subject, payload, StackExchange.Redis.CommandFlags.FireAndForget);
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);

            }
            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            Console.WriteLine("COMPLETE");

            pubConn.Dispose();
            subConn.Dispose();
        }

        void runSuite()
        {
            //runPub("PubOnlyNo", 10000000, 0);
            //runPub("PubOnly8b", 10000000, 8);
            //runPub("PubOnly32b", 10000000, 32);
            //runPub("PubOnly256b", 10000000, 256);
            //runPub("PubOnly512b", 10000000, 512);
            //runPub("PubOnly1k", 1000000, 1024);
            //runPub("PubOnly4k", 500000, 1024 * 4);
            //runPub("PubOnly8k", 100000, 1024 * 8);

            //runPubSubVector3("PubSubVector3", 10000000);

            //runPubSub("PubSubNo", 10000000, 0);
            //runPubSub("PubSub8b", 10000000, 8);
            //runPubSub("PubSub32b", 10000000, 32);
            //runPubSub("PubSub100b", 10000000, 100);
            //runPubSub("PubSub256b", 10000000, 256);
            //runPubSub("PubSub512b", 500000, 512);
            //runPubSub("PubSub1k", 500000, 1024);
            //runPubSub("PubSub4k", 500000, 1024 * 4);
            //runPubSub("PubSub8k", 100000, 1024 * 8);

            {
                
            }
            var key = new NatsKey("foo");
            var serializer = new MessagePackNatsSerializer();
            var l = new List<PublishCommand<Vector3>>();
            for (int i = 0; i < 10000000; i++)
            {
                var p = PublishCommand<Vector3>.Create(key, new Vector3(), serializer);
                l.Add(p);
            }
            foreach (var item in l)
            {
                item.Return();
            }

            RunPubSubAlterNatsVector3("AlterNatsV3", 10000000);
            // RunPubSubAlterNatsVector3("AlterNatsV3", 10000000);

        
            RunPubSubAlterNatsVector3("AlterNatsV3", 10000000);
            {
                var p = PublishCommand<Vector3>.Create(key, new Vector3(), serializer);
                (p as ICommand).Return();
            }
            //RunPubSubAlterNats("AlterNatsNo", 10000000, 0);
            //RunPubSubAlterNats("AlterNats8b", 10000000, 8);
            //RunPubSubAlterNats("AlterNats32b", 10000000, 32);
            //RunPubSubAlterNats("AlterNats100b", 10000000, 100);
            //RunPubSubAlterNats("AlterNats256b", 10000000, 256);
            //RunPubSubAlterNats("AlterNats512b", 500000, 512);
            //RunPubSubAlterNats("AlterNats1k", 500000, 1024);
            //RunPubSubAlterNats("AlterNats4k", 500000, 1024 * 4);
            //RunPubSubAlterNats("AlterNats8k", 100000, 1024 * 8);

            // Redis?
            // RunPubSubRedis("StackExchange.Redis", 10000000, 8);
            // RunPubSubRedis("Redis 100", 10000000, 100);

            // These run significantly slower.
            // req->server->reply->server->req
            //runReqReply("ReqReplNo", 20000, 0);
            //runReqReply("ReqRepl8b", 10000, 8);
            //runReqReply("ReqRepl32b", 10000, 32);
            //runReqReply("ReqRepl256b", 5000, 256);
            //runReqReply("ReqRepl512b", 5000, 512);
            //runReqReply("ReqRepl1k", 5000, 1024);
            //runReqReply("ReqRepl4k", 5000, 1024 * 4);
            //runReqReply("ReqRepl8k", 5000, 1024 * 8);

            //runReqReplyAsync("ReqReplAsyncNo", 20000, 0).Wait();
            //runReqReplyAsync("ReqReplAsync8b", 10000, 8).Wait();
            //runReqReplyAsync("ReqReplAsync32b", 10000, 32).Wait();
            //runReqReplyAsync("ReqReplAsync256b", 5000, 256).Wait();
            //runReqReplyAsync("ReqReplAsync512b", 5000, 512).Wait();
            //runReqReplyAsync("ReqReplAsync1k", 5000, 1024).Wait();
            //runReqReplyAsync("ReqReplAsync4k", 5000, 1024 * 4).Wait();
            //runReqReplyAsync("ReqReplAsync8k", 5000, 1024 * 8).Wait();

            //runPubSubLatency("LatNo", 500, 0);
            //runPubSubLatency("Lat8b", 500, 8);
            //runPubSubLatency("Lat32b", 500, 32);
            //runPubSubLatency("Lat256b", 500, 256);
            //runPubSubLatency("Lat512b", 500, 512);
            //runPubSubLatency("Lat1k", 500, 1024);
            //runPubSubLatency("Lat4k", 500, 1024 * 4);
            //runPubSubLatency("Lat8k", 500, 1024 * 8);
        }
    }
}

[MessagePackObject]
public struct Vector3
{
    [Key(0)]
    public float X;
    [Key(1)]
    public float Y;
    [Key(2)]
    public float Z;
}

