﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlterNats.Tests;

public class CancellationTest
{
    readonly ITestOutputHelper output;

    public CancellationTest(ITestOutputHelper output)
    {
        this.output = output;
    }

    // should check
    // timeout via command-timeout(request-timeout)
    // timeout via connection dispose
    // cancel manually

    [Fact]
    public async Task CommandTimeoutTest()
    {
        await using var server = new NatsServer(output, TransportType.Tcp);

        await using var subConnection = server.CreateClientConnection(NatsOptions.Default with { CommandTimeout = TimeSpan.FromSeconds(1) });
        await using var pubConnection = server.CreateClientConnection(NatsOptions.Default with { CommandTimeout = TimeSpan.FromSeconds(1) });
        await pubConnection.ConnectAsync();

        await subConnection.SubscribeAsync("foo", () =>
        {
            // signalComplete.Pulse();
        });

        var cmd = new SleepWriteCommand("PUB foo 5\r\naiueo", TimeSpan.FromSeconds(10));
        pubConnection.PostDirectWrite(cmd);


        var timeoutException = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pubConnection.PublishAsync("foo", "aiueo");
        });

        timeoutException.Message.Should().Contain("1 seconds elapsing");






    }

    // Queue-full

    // External Cancellation
}


// writer queue can't consume when sleeping
class SleepWriteCommand : ICommand
{
    public bool IsCanceled => false;

    readonly byte[] protocol;
    readonly TimeSpan sleeptime;

    public SleepWriteCommand(string protocol, TimeSpan sleeptime)
    {
        this.protocol = Encoding.UTF8.GetBytes(protocol + "\r\n");
        this.sleeptime = sleeptime;
    }

    public void Return(ObjectPool pool)
    {
    }

    public void SetCancellationTimer(CancellationTimer timer)
    {
    }

    public void Write(ProtocolWriter writer)
    {
        Thread.Sleep(sleeptime);
        writer.WriteRaw(protocol);
    }
}
