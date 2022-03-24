using AlterNats.Commands;
using System.Buffers;
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
        // TODO:wait get INFO?
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

    // TODO:SubscribeAsync?

    public void Publish<T>(string key, T value)
    {
        var command = PublishCommand<T>.Create(key, value, Options.Serializer);
        socketWriter.Post(command);
    }

    // byte[] and ReadOnlyMemory<byte> uses raw publish.
    // TODO:ReadOnlyMemory<byte> overload.
    public void Publish(string key, byte[] value)
    {
        var command = PublishRawCommand.Create(key, value);
        socketWriter.Post(command);
    }

    public IDisposable Subscribe<T>(string key, Action<T> handler)
    {
        return subscriptionManager.Add(key, handler);
    }

    // internal commands.

    internal void PostPong()
    {
        socketWriter.Post(PongCommand.Create());
    }

    internal void PostSubscribe(int subscriptionId, string subject)
    {
        socketWriter.Post(SubscribeCommand.Create(subscriptionId, subject));
    }

    internal void PostUnsubscribe(int subscriptionId)
    {
        socketWriter.Post(UnsubscribeCommand.Create(subscriptionId));
    }

    internal void PublishToClientHandlers(int subscriptionId, in ReadOnlySequence<byte> buffer)
    {
        subscriptionManager.PublishToClientHandlers(subscriptionId, buffer);
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

    // TODO: to internal
    public void DirectWrite(string protocol)
    {
        socketWriter.Post(new DirectWriteCommand(protocol));
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
