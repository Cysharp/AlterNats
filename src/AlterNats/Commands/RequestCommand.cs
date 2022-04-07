using AlterNats.Internal;

namespace AlterNats.Commands;

internal sealed class RequestAsyncCommand<TRequest, TResponse> : AsyncCommandBase<RequestAsyncCommand<TRequest, TResponse>, TResponse>
{
    NatsKey key;
    TRequest? request;
    ReadOnlyMemory<byte> inboxPrefix;
    int id;
    INatsSerializer? serializer;

    RequestAsyncCommand()
    {
    }

    public static RequestAsyncCommand<TRequest, TResponse> Create(ObjectPool pool, in NatsKey key, ReadOnlyMemory<byte> inboxPrefix, int id, TRequest request, INatsSerializer serializer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new RequestAsyncCommand<TRequest, TResponse>();
        }

        result.key = key;
        result.inboxPrefix = inboxPrefix;
        result.id = id;
        result.request = request;
        result.serializer = serializer;

        return result;
    }

    protected override void Reset()
    {
        key = default;
        request = default;
        inboxPrefix = null;
        serializer = null;
        id = 0;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(key, inboxPrefix, id, request, serializer!);
    }
}
