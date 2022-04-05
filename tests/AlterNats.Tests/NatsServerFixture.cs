using Cysharp.Diagnostics;
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AlterNats.Tests;

public class NatsServerFixture : IDisposable
{
    readonly CancellationTokenSource cancellationTokenSource = new();

    public NatsServerFixture()
    {
        var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

        // Start cluster nats servers
        ProcessX.StartAsync($"../../../../../tools/nats-server{ext} -p 14222 -cluster nats://localhost:14248 --cluster_name test-cluster").WaitAsync(cancellationTokenSource.Token);
        ProcessX.StartAsync($"../../../../../tools/nats-server{ext} -p 14223 -cluster nats://localhost:14249 -routes nats://localhost:14248 --cluster_name test-cluster").WaitAsync(cancellationTokenSource.Token);

        // Check for start servers
        Task.Run(async () =>
        {
            using var client1 = new TcpClient();
            using var client2 = new TcpClient();

            while (true)
            {
                client1.Connect("localhost", 14222);
                client2.Connect("localhost", 14223);

                if (client1.Connected && client2.Connected) break;

                await Task.Delay(500);
            }
        }).Wait(5000);
    }

    public void Dispose()
    {
        cancellationTokenSource.Cancel();
    }
}
