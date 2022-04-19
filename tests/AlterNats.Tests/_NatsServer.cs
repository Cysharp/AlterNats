using Cysharp.Diagnostics;
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace AlterNats.Tests;

public class NatsServer : IAsyncDisposable
{
    static readonly string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
    static readonly string natsServerPath = $"../../../../../tools/nats-server{ext}";

    readonly CancellationTokenSource cancellationTokenSource = new();
    readonly ITestOutputHelper outputHelper;
    readonly Task process;

    public int Port { get; }

    bool isDisposed;

    public NatsServer(ITestOutputHelper outputHelper, string argument = "")
    {
        this.outputHelper = outputHelper;
        this.Port = Random.Shared.Next(5000, 6000);
        this.process = ProcessX.StartAsync($"{natsServerPath} -p {Port} {argument}".Trim()).WaitAsync(cancellationTokenSource.Token);

        // Check for start server
        Task.Run(async () =>
        {
            using var client = new TcpClient();
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await client.ConnectAsync("localhost", Port, cancellationTokenSource.Token);
                    if (client.Connected) return;
                }
                catch
                {
                    // ignore
                }

                await Task.Delay(500, cancellationTokenSource.Token);
            }
        }).Wait(5000); // timeout

        outputHelper.WriteLine("OK to Process Start, Port:" + Port);
    }

    public async ValueTask DisposeAsync()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            cancellationTokenSource.Cancel(); // trigger of process kill.
            cancellationTokenSource.Dispose();
            try
            {
                await process; // wait for process exit.
            }
            catch (OperationCanceledException) { }
        }
    }

    public NatsConnection CreateClientConnection() => CreateClientConnection(NatsOptions.Default);

    public NatsConnection CreateClientConnection(NatsOptions options)
    {
        return new NatsConnection(ClientOptions(options));
    }

    public NatsOptions ClientOptions(NatsOptions options)
    {
        return options with
        {
            LoggerFactory = new OutputHelperLoggerFactory(outputHelper),
            Url = $"localhost:{Port}"
        };
    }
}

public class NatsCluster : IAsyncDisposable
{
    readonly ITestOutputHelper outputHelper;

    public NatsServer Server1 { get; }
    public NatsServer Server2 { get; }
    public NatsServer Server3 { get; }

    public NatsCluster(ITestOutputHelper outputHelper)
    {
        var Port1 = Random.Shared.Next(10000, 11000);
        var Port2 = Random.Shared.Next(10000, 11000);
        var Port3 = Random.Shared.Next(10000, 11000);

        this.outputHelper = outputHelper;
        this.Server1 = new NatsServer(outputHelper, $"--cluster_name test-cluster -cluster nats://localhost:{Port1}");
        this.Server2 = new NatsServer(outputHelper, $"--cluster_name test-cluster -cluster nats://localhost:{Port2} -routes nats://localhost:{Port1}");
        this.Server3 = new NatsServer(outputHelper, $"--cluster_name test-cluster -cluster nats://localhost:{Port3} -routes nats://localhost:{Port1}");
    }

    public async ValueTask DisposeAsync()
    {
        await Server1.DisposeAsync();
        await Server2.DisposeAsync();
        await Server3.DisposeAsync();
    }
}
