using AlterNats;
using AlterNats.Commands;
using Cysharp.Diagnostics;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using ZLogger;

var builder = ConsoleApp.CreateBuilder(args);
builder.ConfigureServices(services =>
{

    services.AddNats(4, configureOptions: opt => opt with { Url = "localhost:4222", ConnectOptions = ConnectOptions.Default with { Name = "MyClient" } });

    

});









var app = builder.Build();

app.AddCommands<Runner>();
await app.RunAsync();

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
        await command.SubscribeAsync("foo", () => Console.WriteLine("Yeah"));
        await command.PingAsync();
        await command.PublishAsync("foo");
    }
}
