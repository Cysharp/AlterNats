using System.Buffers;
using MemoryPack;

namespace AlterNats.Tests;

public class MemoryPackNatsSerializerTest
{
    [Fact]
    public void SerializerTest()
    {
        var serializer = new MemoryPackNatsSerializer();

        var data1 = int.MaxValue;
        var data2 = "string";
        var data3 = new SampleClassForMemoryPack(1, "name");

        // serialize
        var bufferWriter1 = new FixedArrayBufferWriter();
        serializer.Serialize(bufferWriter1, data1);
        Assert.Equal(data1, MemoryPackSerializer.Deserialize<int>(new ReadOnlySequence<byte>(bufferWriter1.WrittenMemory)));

        var bufferWriter2 = new FixedArrayBufferWriter();
        serializer.Serialize(bufferWriter2, data2);
        Assert.Equal(data2, MemoryPackSerializer.Deserialize<string>(new ReadOnlySequence<byte>(bufferWriter2.WrittenMemory)));

        var bufferWriter3 = new FixedArrayBufferWriter();
        serializer.Serialize(bufferWriter3, data3);
        Assert.Equal(data3, MemoryPackSerializer.Deserialize<SampleClassForMemoryPack>(new ReadOnlySequence<byte>(bufferWriter3.WrittenMemory)));

        // deserialize
        Assert.Equal(data1, serializer.Deserialize<int>(new ReadOnlySequence<byte>(MemoryPackSerializer.Serialize(data1))));
        Assert.Equal(data2, serializer.Deserialize<string>(new ReadOnlySequence<byte>(MemoryPackSerializer.Serialize(data2))));
        Assert.Equal(data3, serializer.Deserialize<SampleClassForMemoryPack>(new ReadOnlySequence<byte>(MemoryPackSerializer.Serialize(data3))));
    }
}
