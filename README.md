# AlterNats
[![GitHub Actions](https://github.com/Cysharp/AlterNats/workflows/Build-Debug/badge.svg)](https://github.com/Cysharp/AlterNats/actions) [![Releases](https://img.shields.io/github/release/Cysharp/AlterNats.svg)](https://github.com/Cysharp/AlterNats/releases)

An alternative high performance [NATS](https://nats.io/) client for .NET. Zero Allocation/Zero Copy Architecture to achive x2~4 performance compare with official NATS client and StackExchange.Redis's PubSub.

Currently preview, code is not stable so DON'T USE IN PRODUCTION.

> PM> Install-Package [AlterNats](https://www.nuget.org/packages/AlterNats)

```csharp
// Simple usage, create connection
await using var connection = new NatsConnection();
await connection.ConnectAsync();

// for subscriber. await register to NATS server(not means await complete)
await connection.SubscribeAsync<int>("foo", x =>
{
    Console.WriteLine(x);
});

// for publisher.
await connection.PublishAsync("foo", 100);

// Post*** method do FireAndForget operation
connection.PostPublish("foo", 2000);
```

```csharp
// Options is based record so extend with `with` operator
var options = NatsOptions.Default with
{
    LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Information),
    Host = "localhost",
    Port = 4222,
    ConnectOptions = ConnectOptions.Default with { Echo = true, Verbose = false }
};

await using var connection = new NatsConnection(options);
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

License
---
This library is licensed under the MIT License.
