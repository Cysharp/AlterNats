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
        if (type == typeof(byte[]))
        {
            return new ByteArrayMessagePublisher();
        }
        else if (type == typeof(ReadOnlyMemory<byte>))
        {
            return new ReadOnlyMemoryMessagePublisher();
        }

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

internal sealed class ByteArrayMessagePublisher : IMessagePublisher
{
    public void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        byte[] value;
        try
        {
            value = buffer.ToArray();
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Deserialize error during receive subscribed message.");
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
                    var item = ThreadPoolWorkItem<byte[]>.Create((Action<byte[]?>)callback, value);
                    ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                    item.Execute();
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ByteArrayMessagePublisher>().LogError(ex, "Error occured during publish callback.");
            }
            catch { }
        }
    }
}

internal sealed class ReadOnlyMemoryMessagePublisher : IMessagePublisher
{
    public void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        ReadOnlyMemory<byte> value;
        try
        {
            value = buffer.ToArray();
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Deserialize error during receive subscribed message.");
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
                    var item = ThreadPoolWorkItem<ReadOnlyMemory<byte>>.Create((Action<ReadOnlyMemory<byte>>)callback, value);
                    ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Error occured during publish callback.");
            }
            catch { }
        }
    }
}
