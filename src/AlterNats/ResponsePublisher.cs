using AlterNats.Commands;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;

namespace AlterNats;

internal static class ResponsePublisher
{
    // To avoid boxing, cache generic type and invoke it.
    static readonly Func<Type, PublishResponseMessage> createPublisher = CreatePublisher;
    static readonly ConcurrentDictionary<Type, PublishResponseMessage> publisherCache = new();

    public static void Publish(Type type, NatsOptions options, in ReadOnlySequence<byte> buffer, object callback)
    {
        publisherCache.GetOrAdd(type, createPublisher).Invoke(options, buffer, callback);
    }

    static PublishResponseMessage CreatePublisher(Type type)
    {
        // TODO: byte[] publisher???

        var publisher = typeof(ResponseBox<>).MakeGenericType(type)!;
        var instance = Activator.CreateInstance(publisher)!;
        return (PublishResponseMessage)Delegate.CreateDelegate(typeof(PublishResponseMessage), instance, "Publish", false);
    }
}

internal delegate void PublishResponseMessage(NatsOptions options, in ReadOnlySequence<byte> buffer, object callback);

internal sealed class ResponseBox<T> // TResponse
{
    public void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object callback)
    {
        T? value;
        try
        {
            value = options!.Serializer.Deserialize<T>(buffer);
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<IPromise<T>>().LogError(ex, "Deserialize error during receive subscribed message. Type:{0}", typeof(T).Name);
            }
            catch { }
            return;
        }

        try
        {
            try
            {
                // always run on threadpool.
                ((IPromise<T?>)callback).SetResult(value);
            }
            catch (Exception ex)
            {
                options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Error occured during response callback.");
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Error occured during response callback.");
            }
            catch { }
        }
    }
}
