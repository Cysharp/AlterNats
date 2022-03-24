using AlterNats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NatsBenchmark;
using System.Diagnostics;
using ZLogger;


ThreadPool.SetMinThreads(1000, 1000);
// ThreadPool.SetMaxThreads(10, 10);

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
                    x.SetMinimumLevel(LogLevel.Information);
                    x.AddZLoggerConsole();
                })
                .BuildServiceProvider();


            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ILogger<Benchmark>>();
            var options = NatsOptions.Default with
            {
                LoggerFactory = loggerFactory,
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

            var d = subConn.Subscribe<byte[]>(subject, _ =>
            {
                Interlocked.Increment(ref subCount);
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
                pubConn.Publish(subject, payload);
            }

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);

            }
            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            Console.WriteLine("COMPLETE");

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
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

            //runPubSub("PubSubNo", 10000000, 0);
            runPubSub("PubSub8b", 10000000, 8);
            //runPubSub("PubSub32b", 10000000, 32);
            //runPubSub("PubSub256b", 10000000, 256);
            //runPubSub("PubSub512b", 500000, 512);
            //runPubSub("PubSub1k", 500000, 1024);
            //runPubSub("PubSub4k", 500000, 1024 * 4);
            //runPubSub("PubSub8k", 100000, 1024 * 8);

            // TODO:Support No publish
            // RunPubSubAlterNats("PubSubNo", 10000000, 0);
            RunPubSubAlterNats("AlterNats", 10000000, 8);
            //RunPubSubAlterNats("PubSub8b", 10000000, 8);
            //RunPubSubAlterNats("PubSub32b", 10000000, 32);
            //RunPubSubAlterNats("PubSub256b", 10000000, 256);
            //RunPubSubAlterNats("PubSub512b", 500000, 512);
            //RunPubSubAlterNats("PubSub1k", 500000, 1024);
            //RunPubSubAlterNats("PubSub4k", 500000, 1024 * 4);
            //RunPubSubAlterNats("PubSub8k", 100000, 1024 * 8);

            // Redis?
             RunPubSubRedis("StackExchange.Redis", 10000000, 8);

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
