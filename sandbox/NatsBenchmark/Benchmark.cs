// Originally this benchmark code is borrowd from https://github.com/nats-io/nats.net
#nullable disable

// Copyright 2015-2018 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using NATS.Client;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ZLogger;
using System.Text;

namespace NatsBenchmark
{
    public partial class Benchmark
    {
        static readonly long DEFAULT_COUNT = 10000000;

        string url = null;
        long count = DEFAULT_COUNT;
        long payloadSize = 0;
        string subject = "s";
        bool useOldRequestStyle = true;
        string creds = null;

        enum BenchType
        {
            PUB = 0,
            PUBSUB,
            REQREPLY,
            SUITE,
            REQREPLYASYNC,
        };

        void setBenchType(string value)
        {
            switch (value)
            {
                case "PUB":
                    btype = BenchType.PUB;
                    break;
                case "PUBSUB":
                    btype = BenchType.PUBSUB;
                    break;
                case "REQREP":
                    btype = BenchType.REQREPLY;
                    break;
                case "REQREPASYNC":
                    btype = BenchType.REQREPLYASYNC;
                    break;
                case "SUITE":
                    btype = BenchType.SUITE;
                    break;
                default:
                    btype = BenchType.PUB;
                    Console.WriteLine("No type specified.  Defaulting to PUB.");
                    break;
            }
        }

        BenchType btype = BenchType.SUITE;

        void usage()
        {
            Console.WriteLine("benchmark [-h] -type <PUB|PUBSUB|REQREP|REQREPASYNC|SUITE> -url <server url> -count <test count> -creds <creds file> -size <payload size (bytes)>");
        }

        string getValue(IDictionary<string, string> values, string key, string defaultValue)
        {
            if (values.ContainsKey(key))
                return values[key];

            return defaultValue;
        }

        bool parseArgs(string[] args)
        {
            try
            {
                // defaults
                if (args == null || args.Length == 0)
                    return true;

                IDictionary<string, string> strArgs = new Dictionary<string, string>();

                for (int i = 0; i < args.Length; i++)
                {
                    if (i + 1 > args.Length)
                        throw new Exception("Missing argument after " + args[i]);

                    if ("-h".Equals(args[i].ToLower()) ||
                        "/?".Equals(args[i].ToLower()))
                    {
                        usage();
                        return false;
                    }

                    strArgs.Add(args[i], args[i + 1]);
                    i++;
                }

                setBenchType(getValue(strArgs, "-type", "PUB"));

                url = getValue(strArgs, "-url", "nats://localhost:4222");
                count = Convert.ToInt64(getValue(strArgs, "-count", "10000"));
                payloadSize = Convert.ToInt64(getValue(strArgs, "-size", "0"));
                useOldRequestStyle = Convert.ToBoolean(getValue(strArgs, "-old", "false"));
                creds = getValue(strArgs, "-creds", null);

                Console.WriteLine("Running NATS Custom benchmark:");
                Console.WriteLine("    URL:   " + url);
                Console.WriteLine("    Count: " + count);
                Console.WriteLine("    Size:  " + payloadSize);
                Console.WriteLine("    Type:  " + getValue(strArgs, "-type", "PUB"));
                Console.WriteLine("");

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to parse command line args: " + e.Message);
                return false;
            }
        }

        void PrintResults(string testPrefix, Stopwatch sw, long testCount, long msgSize)
        {
            int msgRate = (int)(testCount / sw.Elapsed.TotalSeconds);

            Console.WriteLine(
                "{0}\t{1,10}\t{2,10} msgs/s\t{3,8} kb/s",
                testPrefix, testCount, msgRate, msgRate * msgSize / 1024);
        }

        byte[] generatePayload(long size)
        {
            byte[] data = null;

            if (size == 0)
                return null;

            data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)'a';
            }

            return data;
        }

        void runPub(string testName, long testCount, long testSize)
        {
            byte[] payload = generatePayload(testSize);

            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = url;
            if (creds != null)
            {
                opts.SetUserCredentials(creds);
            }

            using (IConnection c = new ConnectionFactory().CreateConnection(opts))
            {
                Stopwatch sw = sw = Stopwatch.StartNew();

                for (int i = 0; i < testCount; i++)
                {
                    c.Publish(subject, payload);
                }

                sw.Stop();

                PrintResults(testName, sw, testCount, testSize);
            }
        }

        void runPubSub(string testName, long testCount, long testSize)
        {
            object pubSubLock = new object();
            bool finished = false;
            int subCount = 0;

            byte[] payload = generatePayload(testSize);

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

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < testCount; i++)
            {
                pubConn.Publish(subject, payload);
            }

            pubConn.Flush();

            lock (pubSubLock)
            {
                if (!finished)
                    Monitor.Wait(pubSubLock);

            }
            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            pubConn.Close();
            subConn.Close();
        }

        double convertTicksToMicros(long ticks)
        {
            return convertTicksToMicros((double)ticks);
        }

        double convertTicksToMicros(double ticks)
        {
            return ticks / TimeSpan.TicksPerMillisecond * 1000.0;
        }

        void runPubSubLatency(string testName, long testCount, long testSize)
        {
            object subcriberLock = new object();
            bool subscriberDone = false;

            List<long> measurements = new List<long>((int)testCount);

            byte[] payload = generatePayload(testSize);

            ConnectionFactory cf = new ConnectionFactory();
            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = url;
            if (creds != null)
            {
                opts.SetUserCredentials(creds);
            }

            IConnection subConn = cf.CreateConnection(opts);
            IConnection pubConn = cf.CreateConnection(opts);

            Stopwatch sw = new Stopwatch();

            IAsyncSubscription subs = subConn.SubscribeAsync(subject, (sender, args) =>
            {
                sw.Stop();

                measurements.Add(sw.ElapsedTicks);

                lock (subcriberLock)
                {
                    Monitor.Pulse(subcriberLock);
                    subscriberDone = true;
                }
            });

            subConn.Flush();

            for (int i = 0; i < testCount; i++)
            {
                lock (subcriberLock)
                {
                    subscriberDone = false;
                }

                sw.Reset();
                sw.Start();

                pubConn.Publish(subject, payload);
                pubConn.Flush();

                // block on the subscriber finishing - we do not want any
                // overlap in measurements.
                lock (subcriberLock)
                {
                    if (!subscriberDone)
                    {
                        Monitor.Wait(subcriberLock);
                    }
                }
            }

            double latencyAvg = measurements.Average();

            double stddev = Math.Sqrt(
                measurements.Average(
                    v => Math.Pow(v - latencyAvg, 2)
                )
            );

            Console.WriteLine(
                "{0} (us)\t{1} msgs, {2:F2} avg, {3:F2} min, {4:F2} max, {5:F2} stddev",
                testName,
                testCount,
                convertTicksToMicros(latencyAvg),
                convertTicksToMicros(measurements.Min()),
                convertTicksToMicros(measurements.Max()),
                convertTicksToMicros(stddev));

            pubConn.Close();
            subConn.Close();
        }

        void runReqReply(string testName, long testCount, long testSize)
        {
            byte[] payload = generatePayload(testSize);

            ConnectionFactory cf = new ConnectionFactory();

            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = url;
            opts.UseOldRequestStyle = useOldRequestStyle;
            if (creds != null)
            {
                opts.SetUserCredentials(creds);
            }

            IConnection subConn = cf.CreateConnection(opts);
            IConnection pubConn = cf.CreateConnection(opts);

            Thread t = new Thread(() =>
            {
                ISyncSubscription s = subConn.SubscribeSync(subject);
                for (int i = 0; i < testCount; i++)
                {
                    Msg m = s.NextMessage();
                    subConn.Publish(m.Reply, payload);
                    subConn.Flush();
                }
            });
            t.IsBackground = true;
            t.Start();

            Thread.Sleep(1000);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < testCount; i++)
            {
                pubConn.Request(subject, payload);
            }
            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            pubConn.Close();
            subConn.Close();
        }

        async Task runReqReplyAsync(string testName, long testCount, long testSize)
        {
            byte[] payload = generatePayload(testSize);

            ConnectionFactory cf = new ConnectionFactory();

            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = url;
            opts.UseOldRequestStyle = useOldRequestStyle;
            if (creds != null)
            {
                opts.SetUserCredentials(creds);
            }

            IConnection subConn = cf.CreateConnection(opts);
            IConnection pubConn = cf.CreateConnection(opts);

            Thread t = new Thread(() =>
            {
                ISyncSubscription s = subConn.SubscribeSync(subject);
                for (int i = 0; i < testCount; i++)
                {
                    Msg m = s.NextMessage();
                    subConn.Publish(m.Reply, payload);
                    subConn.Flush();
                }
            });
            t.IsBackground = true;
            t.Start();

            Thread.Sleep(1000);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < testCount; i++)
            {
                await pubConn.RequestAsync(subject, payload).ConfigureAwait(false);
            }
            sw.Stop();

            PrintResults(testName, sw, testCount, testSize);

            pubConn.Close();
            subConn.Close();
        }



        public Benchmark(string[] args)
        {
            if (!parseArgs(args))
                return;

            switch (btype)
            {
                case BenchType.SUITE:
                    runSuite();
                    break;
                case BenchType.PUB:
                    runPub("PUB", count, payloadSize);
                    break;
                case BenchType.PUBSUB:
                    runPubSub("PUBSUB", count, payloadSize);
                    break;
                case BenchType.REQREPLY:
                    runReqReply("REQREP", count, payloadSize);
                    break;
                case BenchType.REQREPLYASYNC:
                    runReqReplyAsync("REQREPASYNC", count, payloadSize).Wait();
                    break;
                default:
                    throw new Exception("Invalid Type.");
            }
        }
    }
}
