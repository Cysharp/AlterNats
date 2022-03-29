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

        var command = AsyncConnectCommand.Create(Options.ConnectOptions);
        socketWriter.Post(command);
        await command.AsValueTask();

        waitForConnectSource.TrySetResult(); // signal connected to NatsReadProtocolProcessor loop
        // TODO:wait get INFO?
    }


    public void PostPing()
    {
        socketWriter.Post(PingCommand.Create());
    }

    public ValueTask PingAsync()
    {
        var command = AsyncPingCommand.Create();
        socketWriter.Post(command);
        return command.AsValueTask();
    }

    // TODO:SubscribeAsync?

    public void Publish<T>(string key, T value)
    {
        Publish(new NatsKey(key, true), value);
    }

    public void Publish<T>(in NatsKey key, T value)
    {
        var command = PublishCommand<T>.Create(key, value, Options.Serializer);
        socketWriter.Post(command);
    }

    public ValueTask PublishAsync<T>(in NatsKey key, T value)
    {
        var command = AsyncPublishCommand<T>.Create(key, value, Options.Serializer);
        socketWriter.Post(command);
        return command.AsValueTask();
    }

    // byte[] and ReadOnlyMemory<byte> uses raw publish.
    // TODO:ReadOnlyMemory<byte> overload.

    // TODO:NULL Publish

    public void Publish(string key, byte[] value)
    {
        Publish(new NatsKey(key, true), value);
    }

    public void Publish(NatsKey key, byte[] value)
    {
        var command = PublishBytesCommand.Create(key, value);
        socketWriter.Post(command);
    }

    // TODO:  this API?


    public ValueTask<TResponse> RequestAsync<TRequest, TResponse>(string key, TRequest request)
    {


        throw new NotImplementedException();
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> responseHandler)
    {
        // _INBOX.
        var replyTo = $"{Options.InboxPrefix}{Guid.NewGuid().ToString()}.";

        throw new NotImplementedException();
    }


    // TODO: Remove fire-and-forget subscribe?

    public IDisposable Subscribe<T>(string key, Action<T> handler)
    {
        return subscriptionManager.Add(key, handler);
    }

    public IDisposable Subscribe<T>(NatsKey key, Action<T> handler)
    {
        return subscriptionManager.Add(key.Key, handler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler)
    {
        return subscriptionManager.AddAsync(key, handler);
    }

    // ResponseAsync


    // internal commands.

    internal void PostPong()
    {
        socketWriter.Post(PongCommand.Create());
    }

    internal void PostSubscribe(int subscriptionId, string subject)
    {
        socketWriter.Post(SubscribeCommand.Create(subscriptionId, new NatsKey(subject, true)));
    }

    internal ValueTask SubscribeAsync(int subscriptionId, string subject)
    {
        var command = AsyncSubscribeCommand.Create(subscriptionId, new NatsKey(subject, true));
        socketWriter.Post(command);
        return command.AsValueTask();
    }

    internal void PostUnsubscribe(int subscriptionId)
    {
        socketWriter.Post(UnsubscribeCommand.Create(subscriptionId));
    }

    internal void PublishToClientHandlers(int subscriptionId, in ReadOnlySequence<byte> buffer)
    {
        subscriptionManager.PublishToClientHandlers(subscriptionId, buffer);
    }

    internal void PostDirectWrite(string protocol)
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

    // static Cache operations.

    public static int MaxCommandCacheSize { get; set; } = int.MaxValue;


    public static int GetPublishCommandCacheSize<T>(bool async)
    {
        if (typeof(T) == typeof(byte[]) || typeof(T) == typeof(ReadOnlyMemory<byte>))
        {
            if (async)
            {
                // TODO:AsyncPublishBytes
                // return PublishBytesCommand<T>.GetCacheCount;
                return 0;
            }
            else
            {
                return PublishBytesCommand.GetCacheCount;
            }
        }
        else
        {
            if (async)
            {
                return AsyncPublishCommand<T>.GetCacheCount;
            }
            else
            {
                return PublishCommand<T>.GetCacheCount;
            }
        }
    }

    public static void CachePublishCommand<T>(int cacheCount)
    {
        if (typeof(T) == typeof(byte[]) || typeof(T) == typeof(ReadOnlyMemory<byte>))
        {
            var array = new PublishBytesCommand[cacheCount];
            for (int i = 0; i < cacheCount; i++)
            {
                array[i] = PublishBytesCommand.Create(default, default);
            }
            for (int i = 0; i < cacheCount; i++)
            {
                (array[i] as ICommand).Return();
            }
        }
        else
        {
            var array = new PublishCommand<T>[cacheCount];
            for (int i = 0; i < cacheCount; i++)
            {
                array[i] = PublishCommand<T>.Create(default, default!, default!);
            }
            for (int i = 0; i < cacheCount; i++)
            {
                (array[i] as ICommand).Return();
            }
        }
    }
}
