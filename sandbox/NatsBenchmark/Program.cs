using AlterNats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NatsBenchmark;
using System.Diagnostics;
using ZLogger;

ThreadPool.SetMaxThreads(10, 10);

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
                // logger.ZLogInformation("Here? {0}", subCount);
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
            Console.ReadLine();

            pubConn.DisposeAsync().AsTask().Wait();
            subConn.DisposeAsync().AsTask().Wait();
        }
    }
}
