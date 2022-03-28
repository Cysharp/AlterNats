using MessagePack;
using System.Buffers;

namespace AlterNats;

public class MessagePackNatsSerializer : INatsSerializer
{
    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        return MessagePackSerializer.Deserialize<T>(buffer);
    }

    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        var before = bufferWriter.WrittenCount;
        MessagePackSerializer.Serialize(bufferWriter, value);
        return bufferWriter.WrittenCount - before;
    }
}
