using AlterNats.Commands;
using System.Net.Sockets;

namespace AlterNats;

public enum NatsConnectionState
{
    Closed,
    Open,
    Connecting
}

public class NatsConnection : IAsyncDisposable
{
    readonly Socket socket;
    readonly NatsReadProtocolProcessor socketReader;
    readonly NatsPipeliningSocketWriter socketWriter;
    readonly SubscriptionManager subscriptionManager;

    TaskCompletionSource waitForConnectSource; // when reconnect, make new source.

    // use List for Queue is not performant
    readonly List<PingCommand> pingQueue = new List<PingCommand>();

    public NatsOptions Options { get; }
    public NatsConnectionState ConnectionState { get; private set; }
    public ServerInfo? ServerInfo { get; internal set; } // server info is set when received INFO
    internal Task WaitForConnect => waitForConnectSource.Task;

    public NatsConnection()
        : this(NatsOptions.Default)
    {
    }

    public NatsConnection(NatsOptions options)
    {
        this.Options = options;
        this.ConnectionState = NatsConnectionState.Closed;
        this.waitForConnectSource = new TaskCompletionSource();

        this.socket = new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (Socket.OSSupportsIPv6)
        {
            socket.DualMode = true;
        }

        socket.NoDelay = true;
        socket.SendBufferSize = 0;
        socket.ReceiveBufferSize = 0;

        this.socketWriter = new NatsPipeliningSocketWriter(socket, Options);
        this.socketReader = new NatsReadProtocolProcessor(socket, this);
        this.subscriptionManager = new SubscriptionManager(this);
    }

    /// <summary>
    /// Connect socket and write CONNECT command to nats server.
    /// </summary>
    public async ValueTask ConnectAsync()
    {
        this.ConnectionState = NatsConnectionState.Connecting;
        try
        {
            await socket.ConnectAsync(Options.Host, Options.Port, CancellationToken.None); // TODO:CancellationToken
        }
        catch
        {
            this.ConnectionState = NatsConnectionState.Closed;
            throw;
        }
        this.ConnectionState = NatsConnectionState.Open;

        var command = ConnectCommand.Create(Options.ConnectOptions);
        socketWriter.Post(command);

        waitForConnectSource.TrySetResult(); // signal connected to NatsReadProtocolProcessor loop
    }

    public void Ping()
    {
        socketWriter.Post(LightPingCommand.Create());
    }

    public ValueTask PingAsync()
    {
        var command = PingCommand.Create(pingQueue);
        socketWriter.Post(command);
        return command.AsValueTask();
    }

    /// <summary>Send PONG message to Server.</summary>
    internal void Pong()
    {
        socketWriter.Post(PongCommand.Create());
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

        await socket.DisconnectAsync(false); // TODO:if socket is not connected?
        socket.Dispose();
    }
}