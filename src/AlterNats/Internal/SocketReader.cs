using System.Buffers;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AlterNats.Internal;

internal sealed class SocketReader
{
    readonly Socket socket;
    readonly int bufferSize;

    byte[] buffer;

    public SocketReader(Socket socket, int bufferSize)
    {
        this.buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        this.bufferSize = bufferSize;
        this.socket = socket;
    }

    public ValueTask<int> ReadAsync()
    {
        // return Socket's internal IValueTaskSource directly
        return socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);
    }

    public ReadOnlyMemory<byte> RentReadBuffer(int size)
    {
        var buf = buffer;
        buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        return new ReadOnlyMemory<byte>(buf, 0, size);
    }

    public void ReturnBuffer(ReadOnlyMemory<byte> buffer)
    {
        if (MemoryMarshal.TryGetArray(buffer, out var segment) && segment.Array != null)
        {
            ArrayPool<byte>.Shared.Return(segment.Array);
        }
    }

    //public ReadOnlySequence<byte> CombineBuffer(ReadOnlySequence<byte> segment, ReadOnlyMemory<byte> buffer)
    //{
    //    // var lastSegment = new Segment(buffer);




    //    //new ReadOnlySequence<byte>(segment.Start, 0, 
    //}


}