using AlterNats.Commands;
using System.Buffers;
using System.Net.Sockets;
using System.Text;

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
    readonly RequestResponseManager requestResponseManager;

    TaskCompletionSource waitForConnectSource; // when reconnect, make new source.

    public NatsOptions Options { get; }
    public NatsConnectionState ConnectionState { get; private set; }
    public ServerInfo? ServerInfo { get; internal set; } // server info is set when received INFO
    internal Task WaitForConnect => waitForConnectSource.Task;
    internal ReadOnlyMemory<byte> indBoxPrefix;

    public NatsConnection()
        : this(NatsOptions.Default)
    {
    }

    public NatsConnection(NatsOptions options)
    {
        this.Options = options;
        this.ConnectionState = NatsConnectionState.Closed;
        this.waitForConnectSource = new TaskCompletionSource();
        this.indBoxPrefix = Encoding.ASCII.GetBytes($"{options.InboxPrefix}{Guid.NewGuid()}.");

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
        this.requestResponseManager = new RequestResponseManager(this);
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



    public ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values)
    {
        var command = AsyncPublishBatchCommand<T>.Create(values, Options.Serializer);
        socketWriter.Post(command);
        return command.AsValueTask();
    }

    public ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values)
    {
        var command = AsyncPublishBatchCommand<T>.Create(values, Options.Serializer);
        socketWriter.Post(command);
        return command.AsValueTask();
    }


    public IObservable<T> AsObservable<T>(string key)
    {
        return AsObservable<T>(new NatsKey(key, true));
    }

    public IObservable<T> AsObservable<T>(in NatsKey key)
    {
        return new NatsObservable<T>(this, key);
    }


    public ValueTask<TResponse> RequestAsync<TRequest, TResponse>(in NatsKey key, TRequest request)
    {
        return requestResponseManager.AddAsync<TRequest, TResponse>(key, indBoxPrefix, request)!; // NOTE:return nullable?
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> responseHandler)
    {
        return subscriptionManager.AddRequestHandlerAsync(key.Key, responseHandler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler)
    {
        return subscriptionManager.AddAsync(key, null, handler);
    }



    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler)
    {
        return subscriptionManager.AddAsync(key.Key, null, handler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler)
    {
        return subscriptionManager.AddAsync(key.Key, queueGroup, handler);
    }

    // ResponseAsync


    // internal commands.

    internal void PostPong()
    {
        socketWriter.Post(PongCommand.Create());
    }

    internal ValueTask SubscribeAsync(int subscriptionId, string subject, in NatsKey? queueGroup)
    {
        var command = AsyncSubscribeCommand.Create(subscriptionId, new NatsKey(subject, true), queueGroup);
        socketWriter.Post(command);
        return command.AsValueTask();
    }

    internal void PostUnsubscribe(int subscriptionId)
    {
        socketWriter.Post(UnsubscribeCommand.Create(subscriptionId));
    }

    internal void PostCommand(ICommand command)
    {
        socketWriter.Post(command);
    }

    internal void PublishToClientHandlers(int subscriptionId, in ReadOnlySequence<byte> buffer)
    {
        subscriptionManager.PublishToClientHandlers(subscriptionId, buffer);
    }

    internal void PublishToRequestHandler(int subscriptionId, in NatsKey replyTo, in ReadOnlySequence<byte> buffer)
    {
        subscriptionManager.PublishToRequestHandler(subscriptionId, replyTo, buffer);
    }

    internal void PublishToResponseHandler(int requestId, in ReadOnlySequence<byte> buffer)
    {
        requestResponseManager.PublishToResponseHandler(requestId, buffer);
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
