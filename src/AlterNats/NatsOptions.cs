using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    bool UseThreadPoolCallback,
    string InboxPrefix
)
{
    public static NatsOptions Default = new NatsOptions(
        Host: "localhost",
        Port: 4222,
        ConnectOptions: ConnectOptions.Default,
        Serializer: new JsonNatsSerializer(new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
        LoggerFactory: NullLoggerFactory.Instance,
        WriterBufferSize: 32767,
        ReaderBufferSize: 1048576,
        UseThreadPoolCallback: false,
        InboxPrefix: "_INBOX.");

}
