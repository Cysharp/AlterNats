using System.Buffers;
using MemoryPack;

namespace AlterNats;

public class MemoryPackNatsSerializer: INatsSerializer
{
    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        var before = bufferWriter.WrittenCount;
        MemoryPackSerializer.Serialize(bufferWriter, value);
        return bufferWriter.WrittenCount - before;
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        return MemoryPackSerializer.Deserialize<T>(buffer);
    }
}
