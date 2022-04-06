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
    string InboxPrefix,
    bool NoRandomize,
    TimeSpan PingInterval,
    int MaxPingOut,
    TimeSpan ReconnectWait,
    TimeSpan ReconnectJitter,
    TimeSpan Timeout

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
        InboxPrefix: "_INBOX.",
        NoRandomize: false,
        PingInterval: TimeSpan.FromMinutes(2),
        MaxPingOut: 2,
        ReconnectWait: TimeSpan.FromSeconds(2),
        ReconnectJitter: TimeSpan.FromMilliseconds(100),
        Timeout: TimeSpan.FromSeconds(2)
    );
}
