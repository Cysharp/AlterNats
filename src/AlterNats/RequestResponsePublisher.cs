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

    public static void PublishResponse(Type type, NatsOptions options, in ReadOnlySequence<byte> buffer, object callback)
    {
        publisherCache.GetOrAdd(type, createPublisher).Invoke(options, buffer, callback);
    }

    static PublishResponseMessage CreatePublisher(Type type)
    {
        // TODO: byte[] publisher???

        var publisher = typeof(ResponsePublisher<>).MakeGenericType(type)!;
        var instance = Activator.CreateInstance(publisher)!;
        return (PublishResponseMessage)Delegate.CreateDelegate(typeof(PublishResponseMessage), instance, "Publish", false);
    }
}

internal static class RequestPublisher
{
    // To avoid boxing, cache generic type and invoke it.
    static readonly Func<(Type, Type), PublishRequestMessage> createPublisher = CreatePublisher;
    static readonly ConcurrentDictionary<(Type, Type), PublishRequestMessage> publisherCache = new();

    public static void PublishRequest(Type requestType, Type responseType, NatsConnection connection, in NatsKey replyTo, in ReadOnlySequence<byte> buffer, object callback)
    {
        publisherCache.GetOrAdd((requestType, responseType), createPublisher).Invoke(connection, replyTo, buffer, callback);
    }

    static PublishRequestMessage CreatePublisher((Type requestType, Type responseType) type)
    {
        // TODO: byte[] publisher???

        var publisher = typeof(RequestPublisher<,>).MakeGenericType(type.requestType, type.responseType)!;
        var instance = Activator.CreateInstance(publisher)!;
        return (PublishRequestMessage)Delegate.CreateDelegate(typeof(PublishRequestMessage), instance, "Publish", false);
    }
}

internal delegate void PublishResponseMessage(NatsOptions options, in ReadOnlySequence<byte> buffer, object callback);
internal delegate void PublishRequestMessage(NatsConnection connection, in NatsKey replyTo, in ReadOnlySequence<byte> buffer, object callback);

internal sealed class ResponsePublisher<T>
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
                options!.LoggerFactory.CreateLogger<ResponsePublisher<T>>().LogError(ex, "Deserialize error during receive subscribed message. Type:{0}", typeof(T).Name);
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
                options!.LoggerFactory.CreateLogger<ResponsePublisher<T>>().LogError(ex, "Error occured during response callback.");
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ResponsePublisher<T>>().LogError(ex, "Error occured during response callback.");
            }
            catch { }
        }
    }
}

internal sealed class RequestPublisher<TRequest, TResponse>
{
    public void Publish(NatsConnection connection, in NatsKey replyTo, in ReadOnlySequence<byte> buffer, object callback)
    {
        TRequest? value;
        try
        {
            value = connection.Options.Serializer.Deserialize<TRequest>(buffer);
        }
        catch (Exception ex)
        {
            try
            {
                connection.Options.LoggerFactory.CreateLogger<RequestPublisher<TRequest, TResponse>>().LogError(ex, "Deserialize error during receive subscribed message. Type:{0}", typeof(TRequest).Name);
            }
            catch { }
            return;
        }

        try
        {
            // TODO:UseThreadPoolCallback?
            var response = ((Func<TRequest, TResponse>)callback)(value!);
            connection.Publish(replyTo, response); // send response.
        }
        catch (Exception ex)
        {
            try
            {
                connection.Options.LoggerFactory.CreateLogger<RequestPublisher<TRequest, TResponse>>().LogError(ex, "Error occured during request handler.");
            }
            catch { }
        }
    }
}
