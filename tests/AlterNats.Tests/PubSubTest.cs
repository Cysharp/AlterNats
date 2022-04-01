using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AlterNats.Tests;

public class PubSubTest
{
    [Theory]
    [InlineData(4222, 4222)]
    [InlineData(4222, 4223)]
    [InlineData(4223, 4222)]
    [InlineData(4223, 4223)]
    public async Task Basic(int subPort, int pubPort)
    {
        var random = new Random();
        var messages = Enumerable.Range(0, 10).Select(_ => random.Next(0, 100).ToString()).ToArray();
        AutoResetEvent autoResetEvent = new AutoResetEvent(false);

        autoResetEvent.Reset();
        List<string> results = new();

        var natsKey = new NatsKey(Guid.NewGuid().ToString());

        await using var subConnection = new NatsConnection(NatsOptions.Default with
        {
            Port = subPort
        });

        await subConnection.ConnectAsync();

        using var d = await subConnection.SubscribeAsync<string>(natsKey, x =>
        {
            results.Add(x);

            if (results.Count == 10)
                autoResetEvent.Set();
        });

        await using var pubConnection = new NatsConnection(NatsOptions.Default with
        {
            Port = pubPort
        });

        await pubConnection.ConnectAsync();

        foreach (var message in messages)
        {
            await pubConnection.PublishAsync(natsKey, message);
        }

        var waitResult = autoResetEvent.WaitOne(3000);

        Assert.True(waitResult, "Timeout");
        Assert.Equal(messages, results.ToArray());
    }

    [Theory]
    [InlineData(4222, 4222)]
    [InlineData(4222, 4223)]
    [InlineData(4223, 4222)]
    [InlineData(4223, 4223)]
    public async Task BasicRequest(int subPort, int pubPort)
    {
        var random = new Random();
        var messages = Enumerable.Range(0, 10).Select(_ => random.Next(0, 100).ToString()).ToArray();

        var natsKey = new NatsKey(Guid.NewGuid().ToString());

        await using var subConnection = new NatsConnection(NatsOptions.Default with
        {
            Port = subPort
        });

        await subConnection.ConnectAsync();

        using var d = await subConnection.SubscribeRequestAsync<string, string>(natsKey, x => $"Re{x}");

        await using var pubConnection = new NatsConnection(NatsOptions.Default with
        {
            Port = pubPort
        });

        await pubConnection.ConnectAsync();

        foreach (var message in messages)
        {
            Assert.Equal($"Re{message}", await pubConnection.RequestAsync<string, string>(natsKey, message));
        }
    }

    [Fact]
    public async Task ConnectionException()
    {
        var connection1 = new NatsConnection(NatsOptions.Default with
        {
            Port = 4229
        });

        await Assert.ThrowsAsync<SocketException>(async () => await connection1.ConnectAsync());
    }
}

