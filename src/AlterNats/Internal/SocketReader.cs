using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace AlterNats.Internal;

// When socket is closed/disposed, operation throws SocketClosedException
internal sealed class SocketReader
{
    TcpConnection socket;

    Memory<byte> availableMemory;
    readonly int minimumBufferSize;
    readonly SeqeunceBuilder seqeunceBuilder = new SeqeunceBuilder();
    readonly Stopwatch stopwatch = new Stopwatch();
    readonly ILogger<SocketReader> logger;
    readonly bool isTraceLogging;

    public SocketReader(TcpConnection socket, int minimumBufferSize, ILoggerFactory loggerFactory)
    {
        this.socket = socket;
        this.minimumBufferSize = minimumBufferSize;
        this.logger = loggerFactory.CreateLogger<SocketReader>();
        this.isTraceLogging = logger.IsEnabled(LogLevel.Trace);
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
            int read;
            try
            {
                read = await socket.ReceiveAsync(availableMemory, SocketFlags.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                socket.SignalDisconnected(ex);
                throw new SocketClosedException(ex);
            }

            stopwatch.Stop();
            if (isTraceLogging)
            {
                logger.LogTrace("Socket.ReceiveAsync Size: {0} Elapsed: {1}ms", read, stopwatch.Elapsed.TotalMilliseconds);
            }

            if (read == 0)
            {
                var ex = new SocketClosedException(null);
                socket.SignalDisconnected(ex);
                throw ex;
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
            int read;
            try
            {
                read = await socket.ReceiveAsync(availableMemory, SocketFlags.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                socket.SignalDisconnected(ex);
                throw new SocketClosedException(ex);
            }

            stopwatch.Stop();
            if (isTraceLogging)
            {
                logger.LogTrace("Socket.ReceiveAsync Size: {0} Elapsed: {1}ms", read, stopwatch.Elapsed.TotalMilliseconds);
            }

            if (read == 0)
            {
                var ex = new SocketClosedException(null);
                socket.SignalDisconnected(ex);
                throw ex;
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
