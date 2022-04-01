using AlterNats.Commands;
using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace AlterNats;

// TODO:make more...
internal sealed class RequestResponseManager : IDisposable
{
    internal readonly NatsConnection connection;
    readonly object gate = new object();

    int requestId = 0; // unique id per connection



    // ID: Handler
    Dictionary<int, (Type responseType, object handler)> responseBoxes = new();
    IDisposable? globalSubscription;

    public RequestResponseManager(NatsConnection connection)
    {
        this.connection = connection;
    }



    public async ValueTask<TResponse?> AddAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request)
    {
        // TODO:lock...
        var id = Interlocked.Increment(ref requestId);


        var command = RequestAsyncCommand<TRequest, TResponse>.Create(key, inBoxPrefix, id, request, connection.Options.Serializer);


        // Subscribe connection wide inbox
        if (globalSubscription == null)
        {
            var globalSubscribeKey = $"{Encoding.ASCII.GetString(inBoxPrefix.Span)}*";
            globalSubscription = await connection.SubscribeAsync<byte[]>(globalSubscribeKey, _ => { });
        }


        responseBoxes.Add(id, (typeof(TResponse), command));

        connection.PostCommand(command);


        // TODO:direct return
        return await command.AsValueTask();
    }


    public void PublishToResponseHandler(int id, in ReadOnlySequence<byte> buffer)
    {
        // TODO:lock
        if (responseBoxes.Remove(id, out var box))
        {
            ResponsePublisher.PublishResponse(box.responseType, connection.Options, buffer, box.handler);
        }
    }

    public void Dispose()
    {
        // throw new NotImplementedException();

        foreach (var item in responseBoxes)
        {
            if (item.Value.handler is IPromise p)
            {
                p.SetCanceled(CancellationToken.None);
            }
        }

    }

}
