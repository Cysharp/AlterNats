using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlterNats;

// Non-generics helper
internal static class PublishCallbackThreadPoolWorkItemFactory
{
    static readonly Func<Type, Func<NatsOptions, ReadOnlySequence<byte>, object?[], IThreadPoolWorkItem>> createFactory = CreateFactory;
    static readonly ConcurrentDictionary<Type, Func<NatsOptions, ReadOnlySequence<byte>, object?[], IThreadPoolWorkItem>> factoryCache = new();

    public static IThreadPoolWorkItem Create(Type type, NatsOptions options, ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        return factoryCache.GetOrAdd(type, createFactory).Invoke(options, buffer, callbacks);
    }

    static Func<NatsOptions, ReadOnlySequence<byte>, object?[], IThreadPoolWorkItem> CreateFactory(Type type)
    {
        var method = typeof(PublishCallbackThreadPoolWorkItem<>).MakeGenericType(type).GetMethod("Create", BindingFlags.Static | BindingFlags.Public);
        if (method == null)
        {
            throw new InvalidOperationException("Can't find method.");
        }

        var activator = (Func<NatsOptions, ReadOnlySequence<byte>, object?[], IThreadPoolWorkItem>)Delegate.CreateDelegate(typeof(Func<NatsOptions, ReadOnlySequence<byte>, object?[], IThreadPoolWorkItem>), method);
        return (options, buffer, callbacks) => activator(options, buffer, callbacks);
    }
}

internal sealed class PublishCallbackThreadPoolWorkItem<T> : IThreadPoolWorkItem, IObjectPoolNode<PublishCallbackThreadPoolWorkItem<T>>
{
    static ObjectPool<PublishCallbackThreadPoolWorkItem<T>> pool;

    PublishCallbackThreadPoolWorkItem<T>? nextNode;
    public ref PublishCallbackThreadPoolWorkItem<T>? NextNode => ref nextNode;

    public ReadOnlySequence<byte> Buffer;
    public NatsOptions? Options;
    public object?[]? Callbacks;

    PublishCallbackThreadPoolWorkItem()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PublishCallbackThreadPoolWorkItem<T> Create(NatsOptions options, ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        if (!pool.TryPop(out var item))
        {
            item = new PublishCallbackThreadPoolWorkItem<T>();
        }

        item.Options = options;
        item.Buffer = buffer;
        item.Callbacks = callbacks;
        return item;
    }

    public void Execute()
    {
        var buffer = Buffer;
        var options = Options;
        var callbacks = Callbacks;

        Buffer = default;
        Options = null;
        Callbacks = null;

        if (callbacks != null)
        {
            pool.TryPush(this);

            T? value;
            try
            {
                value = options!.Serializer.Deserialize<T>(buffer);
            }
            catch (Exception ex)
            {
                try
                {
                    options!.LoggerFactory.CreateLogger<PublishCallbackThreadPoolWorkItem<T>>().LogError(ex, "Deserialize error during receive subscribed message. Type:{0}", typeof(T).Name);
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
                    options!.LoggerFactory.CreateLogger<PublishCallbackThreadPoolWorkItem<T>>().LogError(ex, "Error occured during publish callback.");
                }
                catch { }
            }
        }
    }
}