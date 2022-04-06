using MessagePack;
using System.Buffers;

namespace AlterNats.Tests;

public class MessagePackNatsSerializerTest
{
    [Fact]
    public void SerializerTest()
    {
        var serializer = new MessagePackNatsSerializer();

        var data1 = int.MaxValue;
        var data2 = "string";
        var data3 = new SampleClass(1, "name");

        // serialize
        var bufferWriter1 = new FixedArrayBufferWriter();
        serializer.Serialize(bufferWriter1, data1);
        Assert.Equal(data1, MessagePackSerializer.Deserialize<int>(bufferWriter1.WrittenMemory));

        var bufferWriter2 = new FixedArrayBufferWriter();
        serializer.Serialize(bufferWriter2, data2);
        Assert.Equal(data2, MessagePackSerializer.Deserialize<string>(bufferWriter2.WrittenMemory));

        var bufferWriter3 = new FixedArrayBufferWriter();
        serializer.Serialize(bufferWriter3, data3);
        Assert.Equal(data3, MessagePackSerializer.Deserialize<SampleClass>(bufferWriter3.WrittenMemory));

        // deserialize
        Assert.Equal(data1, serializer.Deserialize<int>(new ReadOnlySequence<byte>(MessagePackSerializer.Serialize(data1))));
        Assert.Equal(data2, serializer.Deserialize<string>(new ReadOnlySequence<byte>(MessagePackSerializer.Serialize(data2))));
        Assert.Equal(data3, serializer.Deserialize<SampleClass>(new ReadOnlySequence<byte>(MessagePackSerializer.Serialize(data3))));
    }
}
