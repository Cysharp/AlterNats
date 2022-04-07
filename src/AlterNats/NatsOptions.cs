using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlterNats;

public sealed record NatsOptions
(
    string Url,
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
    TimeSpan Timeout, // TODO:Connect Timeout?
    int CommandPoolSize
)
{
    public static NatsOptions Default = new NatsOptions(
        Url: "nats://localhost:4222",
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
        Timeout: TimeSpan.FromSeconds(2),
        CommandPoolSize: 256
    );

    internal NatsUri[] GetSeedUris()
    {
        var urls = Url.Split(',');
        if (NoRandomize)
        {
            return urls.Select(x => new NatsUri(x)).ToArray();
        }
        else
        {
            return urls.Select(x => new NatsUri(x)).OrderBy(_ => Guid.NewGuid()).ToArray();
        }
    }
}
