using System.Buffers;
using System.Text.Json;

namespace AlterNats;

public interface INatsSerializer
{
    public int Serialize<T>(IBufferWriter<byte> bufferWriter, T? value);
    public T? Deserialize<T>(ReadOnlySequence<byte> buffer);
}

public class JsonNatsSerializer : INatsSerializer
{
    readonly JsonSerializerOptions options;

    [ThreadStatic]
    static Utf8JsonWriter? jsonWriter;

    public JsonNatsSerializer(JsonSerializerOptions options)
    {
        this.options = options;
    }

    public int Serialize<T>(IBufferWriter<byte> bufferWriter, T? value)
    {
        Utf8JsonWriter writer;
        if (jsonWriter == null)
        {
            writer = jsonWriter = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = true
            });
        }
        else
        {
            writer = jsonWriter;
            writer.Reset(bufferWriter);
        }

        JsonSerializer.Serialize(writer, value, options);

        var bytesCommitted = (int)writer.BytesCommitted;
        writer.Reset(NullBufferWriter.Instance);
        return bytesCommitted;
    }

    public T? Deserialize<T>(ReadOnlySequence<byte> buffer)
    {
        var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
        return JsonSerializer.Deserialize<T>(ref reader, options);
    }

    sealed class NullBufferWriter : IBufferWriter<byte>
    {
        internal static readonly IBufferWriter<byte> Instance = new NullBufferWriter();

        public void Advance(int count)
        {
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            return Array.Empty<byte>();
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            return Array.Empty<byte>();
        }
    }
}
