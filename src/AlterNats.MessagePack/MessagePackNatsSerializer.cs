using MessagePack;
using System.Buffers;

namespace AlterNats;

public class MessagePackNatsSerializer : INatsSerializer
{
    readonly MessagePackSerializerOptions? options;

    public MessagePackNatsSerializer()
    {

    }

    public MessagePackNatsSerializer(MessagePackSerializerOptions options)
    {
        this.options = options;
    }

    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        var before = bufferWriter.WrittenCount;
        MessagePackSerializer.Serialize(bufferWriter, value, options);
        return bufferWriter.WrittenCount - before;
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        return MessagePackSerializer.Deserialize<T>(buffer, options);
    }
}
