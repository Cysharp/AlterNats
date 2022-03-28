using AlterNats.Commands;
using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AlterNats;

// TODO:make more...
internal sealed class RequestResponseManager
{
    internal readonly NatsConnection connection;
    readonly object gate = new object();

    int requestId = 0; // unique id per connection



    Dictionary<ReadOnlyMemory<byte>, (Type responseType, object handler)> responseBoxes = new();

    public RequestResponseManager(NatsConnection connection)
    {
        this.connection = connection;
    }



    public string Add(string key, TaskCompletionSource taskCompletionSource)
    {
        // Subscribe per connection.
        // _INBOX.RANDOM-GUID.ID

        var id = Interlocked.Increment(ref requestId);

        // TODO:...
        var replyTo = $"{connection.Options.InboxPrefix}{Guid.NewGuid().ToString()}.{id}";




        // responseBox[replyTo] = taskCompletionSource;

        return replyTo;
    }




    public void PublishToResponseHandler(ReadOnlyMemory<byte> replyTo, in ReadOnlySequence<byte> buffer)
    {
        Type? responseType = null;
        object? handler;

        lock (gate)
        {
            if (responseBoxes.Remove(replyTo, out var box))
            {
                (responseType, handler) = box;
            }
            else
            {
                handler = null;
            }
        }

        if (handler != null && responseType != null)
        {
            ResponsePublisher.Publish(responseType, connection.Options, buffer, handler);
        }
    }
}
