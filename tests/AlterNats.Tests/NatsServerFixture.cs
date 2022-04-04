using Cysharp.Diagnostics;
using System;
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

        ProcessX.StartAsync($"../../../../../tools/nats-server{ext} -p 14222 -cluster nats://localhost:14248 --cluster_name test-cluster").WaitAsync(cancellationTokenSource.Token);
        ProcessX.StartAsync($"../../../../../tools/nats-server{ext} -p 14223 -cluster nats://localhost:14249 -routes nats://localhost:14248 --cluster_name test-cluster").WaitAsync(cancellationTokenSource.Token);

        Task.Delay(5000).Wait();
    }

    public void Dispose()
    {
        cancellationTokenSource.Cancel();
    }
}
