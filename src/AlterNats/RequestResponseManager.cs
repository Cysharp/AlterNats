using AlterNats.Commands;
using System.Buffers;
using System.Buffers.Text;

namespace AlterNats;

// TODO:make more...
internal sealed class RequestResponseManager
{
    internal readonly NatsConnection connection;
    readonly object gate = new object();

    int requestId = 0; // unique id per connection



    // ID: Handler
    Dictionary<int, (Type responseType, object handler)> responseBoxes = new();

    public RequestResponseManager(NatsConnection connection)
    {
        this.connection = connection;
    }



    public string Add<TRequest, TResponse>(string key, TRequest request)
    {
        // Subscribe per connection.
        // _INBOX.RANDOM-GUID.ID

        var id = Interlocked.Increment(ref requestId);

        // TODO:...
        var replyTo = $"{connection.Options.InboxPrefix}{Guid.NewGuid()}.{id}";

        


        // var command = RequestAsyncCommand<TRequest, TResponse>.Create(request);


        // responseBoxes.Add(id, (typeof(TResponse), command));

        return replyTo;
    }




    public void PublishToResponseHandler(ReadOnlySpan<byte> replyTo, in ReadOnlySequence<byte> buffer)
    {
        // Parse: _INBOX.RANDOM-GUID.ID
        var lastIndex = replyTo.LastIndexOf((byte)'.');
        if (lastIndex == -1) return;

        if (!Utf8Parser.TryParse(replyTo.Slice(lastIndex), out int id, out _))
        {
            return;
        }

        if (responseBoxes.Remove(id, out var box))
        {
            ResponsePublisher.Publish(box.responseType, connection.Options, buffer, box.handler);
        }
    }
}
