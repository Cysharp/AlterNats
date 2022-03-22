using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlterNats;

public sealed record NatsOptions
(
    string Host,
    int Port,
    ConnectOptions ConnectOptions,
    INatsSerializer Serializer,
    ILoggerFactory LoggerFactory,
    int MaxBatchCount,
    int ReaderBufferSize
)
{
    const string DefaultHost = "localhost";
    const int DefaultPort = 4222;
    const int DefaultMaxBatchCount = 100;
    const int DefaultReaderBufferSize = 1048576; // 1MB

    // TODO:not null, default serializer
    public static NatsOptions Default = new NatsOptions(
        Host: DefaultHost,
        Port: DefaultPort,
        ConnectOptions: ConnectOptions.Default,
        Serializer: new JsonNatsSerializer(new JsonSerializerOptions()),
        LoggerFactory: NullLoggerFactory.Instance,
        MaxBatchCount: DefaultMaxBatchCount,
        ReaderBufferSize: DefaultReaderBufferSize);

}

public interface INatsSerializer
{
    public T? Deserialize<T>(ReadOnlySequence<byte> buffer);
}

public class JsonNatsSerializer : INatsSerializer
{
    readonly JsonSerializerOptions options;

    public JsonNatsSerializer(JsonSerializerOptions options)
    {
        this.options = options;
    }

    public T? Deserialize<T>(ReadOnlySequence<byte> buffer)
    {
        var reader = new Utf8JsonReader(buffer); // Utf8JsonReader is ref struct, no allocate.
        return JsonSerializer.Deserialize<T>(ref reader, options);
    }
}