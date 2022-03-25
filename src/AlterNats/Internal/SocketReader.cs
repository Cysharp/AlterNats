using System.Buffers;
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


    public SocketReader(Socket socket, int minimumBufferSize, CancellationToken cancellationToken)
    {
#if DEBUG
        this.socket = new SocketWrapper(socket);
#else
        this.socket = socket;
#endif
        this.minimumBufferSize = minimumBufferSize;
        this.cancellationToken = cancellationToken;
    }

#if DEBUG
    internal SocketReader(ISocket socket, int minimumBufferSize, CancellationToken cancellationToken)
    {
        this.socket = socket;
        this.minimumBufferSize = minimumBufferSize;
        this.cancellationToken = cancellationToken;
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
        var read = await socket.ReceiveAsync(availableMemory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            throw new Exception(); // TODO: end of read.
        }

        seqeunceBuilder.Append(availableMemory.Slice(0, read));
        availableMemory = availableMemory.Slice(read);
        return seqeunceBuilder.ToReadOnlySequence();
    }

    // ReadAtLeastAsync
    public ValueTask<ReadOnlySequence<byte>> ReadAtLeastAsync(int minimumSize)
    {
        if (seqeunceBuilder.Count >= minimumSize)
        {
            return new ValueTask<ReadOnlySequence<byte>>(seqeunceBuilder.ToReadOnlySequence());
        }

        return Core(minimumSize);

        [AsyncMethodBuilderAttribute(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        async ValueTask<ReadOnlySequence<byte>> Core(int minimumSize)
        {
            var totalRead = 0;
            do
            {
                if (availableMemory.Length == 0)
                {
                    availableMemory = ArrayPool<byte>.Shared.Rent(minimumBufferSize);
                }
                var read = await socket.ReceiveAsync(availableMemory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
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
    }

    [AsyncMethodBuilderAttribute(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ReadOnlySequence<byte>> ReadUntilReceiveNewLineAsync(int minimumSize)
    {
        while (true)
        {
            if (availableMemory.Length == 0)
            {
                availableMemory = ArrayPool<byte>.Shared.Rent(minimumBufferSize);
            }
            var read = await socket.ReceiveAsync(availableMemory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
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
