using AlterNats.Commands;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace AlterNats;

public class NatsConnection : IAsyncDisposable
{
    readonly TcpClient tcpClient;
    readonly Stream stream;
    readonly NatsStreamReader streamReader;
    readonly NatsPipeliningStreamWriter streamWriter;
    readonly SubscriptionManager subscriptionManager;

    // use List for Queue is not performant
    readonly List<PingCommand> pingQueue = new List<PingCommand>();

    public NatsOptions Options { get; }

    public NatsConnection()
    {
        this.Options = NatsOptions.Default; // TODO:

        // TODO: use Raw Socket?
        this.tcpClient = new TcpClient();
        tcpClient.Connect("localhost", 4222); // when? here?

        this.stream = tcpClient.GetStream();
        this.streamWriter = new NatsPipeliningStreamWriter(stream, Options);
        this.streamReader = new NatsStreamReader(stream, this);
        this.subscriptionManager = new SubscriptionManager(this);
    }

    public ValueTask PingAsync()
    {
        var command = PingCommand.Create(pingQueue);
        streamWriter.Post(command);
        return command.AsValueTask();
    }


    /// <summary>Send PONG message to Server.</summary>
    internal void Pong()
    {
        streamWriter.Post(PongCommand.Create());
    }

    // SubscribeAsync???

    public void Subscribe<T>(string key, int subscriptionId, Action<T> handler)
    {
        // SubscribeCommand.Create(
    }

    public void Subscribe(NatsKey natsKey)
    {


    }

    // when receives PONG, signal PING Promise.
    internal void SignalPingCommand()
    {
        lock (pingQueue)
        {
            if (pingQueue.Count != 0)
            {
                var p = pingQueue[0];
                pingQueue.RemoveAt(0);
            }
        }

    }

    public async ValueTask DisposeAsync()
    {
        await streamWriter.DisposeAsync().ConfigureAwait(false);
        await streamReader.DisposeAsync().ConfigureAwait(false);
        subscriptionManager.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);
        tcpClient.Dispose();
    }
}