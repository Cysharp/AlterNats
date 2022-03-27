using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace AlterNats.Internal;

#if DEBUG

// for unit-testing.

internal interface ISocket
{
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default(CancellationToken));
}

internal sealed class SocketWrapper : ISocket
{
    readonly Socket socket;

    public SocketWrapper(Socket socket)
    {
        this.socket = socket;
    }

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default)
    {
        return socket.ReceiveAsync(buffer, socketFlags, cancellationToken);
    }
}

#endif

internal sealed class SocketReader
{
#if DEBUG
    ISocket socket;
#else
    Socket socket;
#endif

    Memory<byte> availableMemory;
    readonly CancellationToken cancellationToken;
    readonly int minimumBufferSize;
    readonly SeqeunceBuilder seqeunceBuilder = new SeqeunceBuilder();
    readonly Stopwatch stopwatch = new Stopwatch();
    readonly ILogger<SocketReader> logger;
    readonly bool isTraceLogging;

    public SocketReader(Socket socket, int minimumBufferSize, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
#if DEBUG
        this.socket = new SocketWrapper(socket);
#else
        this.socket = socket;
#endif
        this.minimumBufferSize = minimumBufferSize;
        this.cancellationToken = cancellationToken;
        this.logger = loggerFactory.CreateLogger<SocketReader>();
        this.isTraceLogging = logger.IsEnabled(LogLevel.Trace);
    }

#if DEBUG
    internal SocketReader(ISocket socket, int minimumBufferSize, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        this.socket = socket;
        this.minimumBufferSize = minimumBufferSize;
        this.cancellationToken = cancellationToken;
        this.logger = loggerFactory.CreateLogger<SocketReader>();
        this.isTraceLogging = logger.IsEnabled(LogLevel.Trace);
    }
#endif

    [AsyncMethodBuilderAttribute(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ReadOnlySequence<byte>> ReadAsync()
    {
        if (availableMemory.Length == 0)
        {
            // rented array is returned from SequenceBuilder.AdvanceTo
            availableMemory = ArrayPool<byte>.Shared.Rent(minimumBufferSize);
        }

        stopwatch.Restart();
        var read = await socket.ReceiveAsync(availableMemory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        if (isTraceLogging)
        {
            logger.LogInformation("Socket.ReceiveAsync Size: {0} Elapsed: {1}ms", read, stopwatch.Elapsed.TotalMilliseconds);
        }

        if (read == 0)
        {
            throw new Exception(); // TODO: end of read.
        }

        seqeunceBuilder.Append(availableMemory.Slice(0, read));
        availableMemory = availableMemory.Slice(read);
        return seqeunceBuilder.ToReadOnlySequence();
    }

    [AsyncMethodBuilderAttribute(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ReadOnlySequence<byte>> ReadAtLeastAsync(int minimumSize)
    {
        var totalRead = 0;
        do
        {
            if (availableMemory.Length == 0)
            {
                availableMemory = ArrayPool<byte>.Shared.Rent(minimumBufferSize);
            }

            stopwatch.Restart();
            var read = await socket.ReceiveAsync(availableMemory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (isTraceLogging)
            {
                logger.LogInformation("Socket.ReceiveAsync Size: {0} Elapsed: {1}ms", read, stopwatch.Elapsed.TotalMilliseconds);
            }

            if (read == 0)
            {
                throw new Exception(); // TODO: end of read.
            }
            totalRead += read;
            seqeunceBuilder.Append(availableMemory.Slice(0, read));
            availableMemory = availableMemory.Slice(read);
        } while (totalRead < minimumSize);

        return seqeunceBuilder.ToReadOnlySequence();
    }

    [AsyncMethodBuilderAttribute(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ReadOnlySequence<byte>> ReadUntilReceiveNewLineAsync()
    {
        while (true)
        {
            if (availableMemory.Length == 0)
            {
                availableMemory = ArrayPool<byte>.Shared.Rent(minimumBufferSize);
            }

            stopwatch.Restart();
            var read = await socket.ReceiveAsync(availableMemory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (isTraceLogging)
            {
                logger.LogInformation("Socket.ReceiveAsync Size: {0} Elapsed: {1}ms", read, stopwatch.Elapsed.TotalMilliseconds);
            }

            if (read == 0)
            {
                throw new Exception(); // TODO: end of read.
            }

            var appendMemory = availableMemory.Slice(0, read);
            seqeunceBuilder.Append(appendMemory);
            availableMemory = availableMemory.Slice(read);

            if (appendMemory.Span.Contains((byte)'\n'))
            {
                break;
            }
        }

        return seqeunceBuilder.ToReadOnlySequence();
    }

    public void AdvanceTo(SequencePosition start)
    {
        seqeunceBuilder.AdvanceTo(start);
    }
}
