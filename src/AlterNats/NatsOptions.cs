using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AlterNats;

public sealed record NatsOptions
(
    string Host,
    int Port,
    ConnectOptions ConnectOptions,
    INatsSerializer Serializer,
    ILoggerFactory LoggerFactory,
    int WriterBufferSize,
    int ReaderBufferSize,
    bool UseThreadPoolCallback
)
{
    const string DefaultHost = "localhost";
    const int DefaultPort = 4222;
    const int DefaultWriterBufferSize = 32768;
    const int DefaultReaderBufferSize = 1048576; // 1MB

    // TODO:not null, default serializer
    public static NatsOptions Default = new NatsOptions(
        Host: DefaultHost,
        Port: DefaultPort,
        ConnectOptions: ConnectOptions.Default,
        Serializer: new JsonNatsSerializer(new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
        LoggerFactory: NullLoggerFactory.Instance,
        WriterBufferSize: DefaultWriterBufferSize,
        ReaderBufferSize: DefaultReaderBufferSize,
        UseThreadPoolCallback: false);

}

