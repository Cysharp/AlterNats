using AlterNats.Commands;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace AlterNats;

public class NatsConnection : IAsyncDisposable
{
    readonly Socket socket;
    readonly NatsReadProtocolProcessor socketReader;
    readonly NatsPipeliningSocketWriter socketWriter;
    readonly SubscriptionManager subscriptionManager;

    // use List for Queue is not performant
    readonly List<PingCommand> pingQueue = new List<PingCommand>();

    public NatsOptions Options { get; }
    public ServerInfo? ServerInfo { get; internal set; } // server info is set when received INFO

    public NatsConnection()
        : this(NatsOptions.Default)
    {
    }

    public NatsConnection(NatsOptions options)
    {
        this.Options = options;
        this.socket = new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (Socket.OSSupportsIPv6)
        {
            socket.DualMode = true;
        }

        socket.NoDelay = true;
        socket.SendBufferSize = 0;
        socket.ReceiveBufferSize = 0;

        socket.Connect("localhost", 4222); // TODO: connect here?

        this.socketWriter = new NatsPipeliningSocketWriter(socket, Options);
        this.socketReader = new NatsReadProtocolProcessor(socket, this);
        this.subscriptionManager = new SubscriptionManager(this);
    }


    public ValueTask PingAsync()
    {
        var command = PingCommand.Create(pingQueue);
        socketWriter.Post(command);
        return command.AsValueTask();
    }


    /// <summary>Send PONG message to Server.</summary>
    public void Pong()
    {
        socketWriter.Post(PongCommand.Create());
    }

    public void Ping()
    {
        socketWriter.Post(LightPingCommand.Create());
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
                // TODO:????
                var p = pingQueue[0];
                pingQueue.RemoveAt(0);
            }
        }

    }

    public async ValueTask DisposeAsync()
    {
        await socketWriter.DisposeAsync().ConfigureAwait(false);
        await socketReader.DisposeAsync().ConfigureAwait(false);
        subscriptionManager.Dispose();

        await socket.DisconnectAsync(false);
        socket.Dispose();
    }
}