# AlterNats
[![GitHub Actions](https://github.com/Cysharp/AlterNats/workflows/Build-Debug/badge.svg)](https://github.com/Cysharp/AlterNats/actions) [![Releases](https://img.shields.io/github/release/Cysharp/AlterNats.svg)](https://github.com/Cysharp/AlterNats/releases)

An alternative high performance [NATS](https://nats.io/) client for .NET. Zero Allocation and Zero Copy Architecture to achive x2~4 performance compare with [official NATS client](https://github.com/nats-io/nats.net) and [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis)'s PubSub.

![image](https://user-images.githubusercontent.com/46207/164392256-46d09111-ec70-4cf3-b33d-38dc5d258455.png)

The most major PubSub Solution in C# is using Redis and StackExchange.Redis. Redis also has a managed service and has a well-designed client library. However, Redis is primarily a KVS and functions poorly as a PubSub.

* Lack of monitoring for PubSub
* Lack of clustering support for PubSub
* Unbalanced pricing for managed services(not much memory is needed for PubSub)
* Performance

Since NATS is specialized for PubSub, it has a rich system for that purpose, and its performance seems to be perfect. The only drawback is that there is no managed service, but if you use NATS as a pure PubSub, you do not need to think about the persistence process, so I think it is one of the easiest middlewares to operate.


TODO: Reason for why not official client? why fast?

Getting Started
---
Package is published on NuGet.

> PM> Install-Package [AlterNats](https://www.nuget.org/packages/AlterNats)

Here is the simple example.

```csharp
// create connection(default, connect to nats://localhost:4222)
await using var conn = new NatsConnection();

// for subscriber. await register to NATS server(not means await complete)
var subscription = await conn.SubscribeAsync<Person>("foo", x =>
{
    Console.WriteLine($"Received {x}");
});

// for publisher.
await conn.PublishAsync("foo", new Person(30, "bar"));

// unsubscribe
subscription.Dipose();
```

Configure option adopts immutable options with `with` operator.
 
```csharp
// Options is immutable and can configure `with` operator
var options = NatsOptions.Default with
{
    Url = "nats://127.0.0.1:9999",
    LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Information),
    Serializer = new MessagePackNatsSerializer(),
    ConnectOptions = ConnectOptions.Default with
    {
        Echo = true,
        Username = "foo",
        Password = "bar",
    }
};

await using var conn = new NatsConnection(options);
```

In default, internal error is not logged so recommend to setup LoggerFactory. AlterNats has builtin simple `MinimumConsoleLoggerFactory` however for production use, use `Microsoft.Extensions.Logging` and setup your favorite logger. [Cysharp/ZLogger](https://github.com/Cysharp/ZLogger/) is especially recommended to keep high performance.

```csharp
// You're using Generic Host(Microsoft.Extensions.Hositng) or ASP.NET Core, you can get ILoggerFactory from the built-in pipeline.
// Otherwise, build it yourself from ServiceCollection.
var provider = new ServiceCollection()
    .AddLogging(x =>
    {
        x.ClearProviders();
        x.SetMinimumLevel(LogLevel.Information);
        x.AddZLoggerConsole();
    })
    .BuildServiceProvider();

var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

var options = NatsOptions.Default with
{
    LoggerFactory = loggerFactory
};

await using var connection = new NatsConnection(options);
```

Minimum RPC
---
```csharp
// Server
await connection.SubscribeRequestAsync<FooRequest, FooResponse>("foobar.key", req =>
{
    return new FooResponse();
});

// Client
var response = await connection.RequestAsync<FooRequest, FooResponse>("foobar.key", new FooRequest());
```

NatsConnection
---

* `Options`
* `ServerInfo`
* `AsObservable()`
* `ConnectAsync()`
* `DisposeAsync()`
* `PingAsync()`
* `PostPing()`
* `PostPublish()`
* `PublishAsync()`
* `PublishBatchAsync()`
* `RequestAsync()`
* `SubscribeAsync()`
* `SubscribeRequestAsync()`


### Stats

```csharp
public NatsStats GetStats() => counter.ToStats();


public readonly record struct NatsStats
(
    long SentBytes,
    long ReceivedBytes,
    long PendingMessages,
    long SentMessages,
    long ReceivedMessages,
    long SubscriptionCount
);
```


## INatsCommand


```csharp
public interface INatsCommand
{
    IObservable<T> AsObservable<T>(in NatsKey key);
    IObservable<T> AsObservable<T>(string key);
    ValueTask FlushAsync();
    ValueTask<TimeSpan> PingAsync();
    void PostDirectWrite(byte[] protocol);
    void PostDirectWrite(DirectWriteCommand command);
    void PostDirectWrite(string protocol, int repeatCount = 1);
    void PostPing();
    void PostPublish(in NatsKey key);
    void PostPublish(in NatsKey key, byte[] value);
    void PostPublish(in NatsKey key, ReadOnlyMemory<byte> value);
    void PostPublish(string key);
    void PostPublish(string key, byte[] value);
    void PostPublish(string key, ReadOnlyMemory<byte> value);
    void PostPublish<T>(in NatsKey key, T value);
    void PostPublish<T>(string key, T value);
    void PostPublishBatch<T>(IEnumerable<(NatsKey, T?)> values);
    void PostPublishBatch<T>(IEnumerable<(string, T?)> values);
    ValueTask PublishAsync(in NatsKey key);
    ValueTask PublishAsync(in NatsKey key, byte[] value);
    ValueTask PublishAsync(in NatsKey key, ReadOnlyMemory<byte> value);
    ValueTask PublishAsync(string key);
    ValueTask PublishAsync(string key, byte[] value);
    ValueTask PublishAsync(string key, ReadOnlyMemory<byte> value);
    ValueTask PublishAsync<T>(in NatsKey key, T value);
    ValueTask PublishAsync<T>(string key, T value);
    ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values);
    ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<T> handler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler);
    ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(NatsKey key, TRequest request, CancellationToken cancellationToken = default);
    ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(string key, TRequest request, CancellationToken cancellationToken = default);
    ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action handler);
    ValueTask<IDisposable> SubscribeAsync(string key, Action handler);
    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler);
}
```


NatsConnectionPool
---


NatsOptions
---

```
string Url,
ConnectOptions ConnectOptions,
INatsSerializer Serializer,
ILoggerFactory LoggerFactory,
int WriterBufferSize,
int ReaderBufferSize,
bool UseThreadPoolCallback,
string InboxPrefix,
bool NoRandomize,
TimeSpan PingInterval,
int MaxPingOut,
TimeSpan ReconnectWait,
TimeSpan ReconnectJitter,
TimeSpan ConnectTimeout,
int CommandPoolSize,
TimeSpan RequestTimeout
```


Serialization
---
In default, serializer uses System.Text.Json. for more performant serialization, you can use MessagePack extension package.

> PM> Install-Package [AlterNats.MessagePack](https://www.nuget.org/packages/AlterNats.MessagePack)

```csharp
var options = NatsOptions.Default with
{
    Serializer = new MessagePackNatsSerializer(),
};
```

Hosting
---


Performance
---


Limitation
---
Currently, AlterNats is not oriented toward compatibility and comprehensiveness, specializes in performance against Core NATS. Therefore, these features are not supported.

* TLS
* JetStream

License
---
This library is licensed under the MIT License.
