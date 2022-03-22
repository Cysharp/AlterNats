using System.Buffers;
using System.Runtime.CompilerServices;

namespace AlterNats.Commands;

internal sealed class FixedArrayBufferWriter : IBufferWriter<byte>
{
    byte[] buffer;
    int written;

    public ReadOnlyMemory<byte> WrittenMemory => buffer.AsMemory(0, written);

    public FixedArrayBufferWriter()
    {
        buffer = new byte[65535];
        written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        written += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        if (buffer.Length - written < sizeHint)
        {
            Resize(sizeHint);
        }
        return buffer.AsMemory(written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (buffer.Length - written < sizeHint)
        {
            Resize(sizeHint);
        }
        return buffer.AsSpan(written);
    }

    void Resize(int sizeHint)
    {
        Array.Resize(ref buffer, Math.Max(sizeHint, buffer.Length * 2));
    }
}
