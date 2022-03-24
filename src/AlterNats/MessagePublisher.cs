using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;

namespace AlterNats;

internal static class MessagePublisher
{
    static readonly Func<Type, IMessagePublisher> createPublisher = CreatePublisher;
    static readonly ConcurrentDictionary<Type, IMessagePublisher> publisherCache = new();

    public static void Publish(Type type, NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        publisherCache.GetOrAdd(type, createPublisher).Publish(options, buffer, callbacks);
    }

    static IMessagePublisher CreatePublisher(Type type)
    {
        var publisher = typeof(MessagePublisher<>).MakeGenericType(type)!;
        return (IMessagePublisher)Activator.CreateInstance(publisher)!;
    }
}

// To avoid boxing, cache generic type and invoke it.
internal interface IMessagePublisher
{
    void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks);
}

internal sealed class MessagePublisher<T> : IMessagePublisher
{
    public void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
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
                options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Deserialize error during receive subscribed message. Type:{0}", typeof(T).Name);
            }
            catch { }
            return;
        }

        try
        {
            foreach (var callback in callbacks!)
            {
                if (callback != null)
                {
                    var item = ThreadPoolWorkItem<T>.Create((Action<T?>)callback, value);
                    ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Error occured during publish callback.");
            }
            catch { }
        }
    }
}