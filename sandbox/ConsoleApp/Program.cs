﻿using AlterNats;
using AlterNats.Commands;
using Cysharp.Diagnostics;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using ZLogger;

var builder = ConsoleApp.CreateBuilder(args);
builder.ConfigureServices(services =>
{
    services.AddNats(poolSize: 4, configureOptions: opt => opt with { Url = "localhost:4222", ConnectOptions = ConnectOptions.Default with { Name = "MyClient" } });
});



// create connection(default, connect to nats://localhost:4222)


// var conn = new NatsConnectionPool(1).GetConnection();


await using var conn = new NatsConnection();
conn.OnConnectingAsync = async x =>
{
    var health = await new HttpClient().GetFromJsonAsync<NatsHealth>($"http://{x.Host}:8222/healthz");
    if (health == null || health.status != "ok") throw new Exception();

    return x;
};




// Server
await conn.SubscribeRequestAsync("foobar",
    (int x) => $"Hello {x}"
    );

// Client(response: "Hello 100")
var response = await conn.RequestAsync<int, string>("foobar", 100);






// subscribe
var subscription = await conn.SubscribeAsync<Person>("foo", (s,x) =>
{
    Console.WriteLine($"Received {x}");
});

// publish
await conn.PublishAsync("foo", new Person(30, "bar"));



// Options can configure `with` expression
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
    },
};




var app = builder.Build();

app.AddCommands<Runner>();
await app.RunAsync();

public record Person(int Age, string Name);

public class Runner : ConsoleAppBase
{
    readonly INatsCommand command;

    public Runner(INatsCommand command)
    {
        this.command = command;
    }

    [RootCommand]
    public async Task Run()
    {
        await command.SubscribeAsync("foo", _ => Console.WriteLine("Yeah"));
        await command.PingAsync();
        await command.PublishAsync("foo");
    }
}

public record NatsHealth(string status);
