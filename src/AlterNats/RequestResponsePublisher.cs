﻿using AlterNats.Commands;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AlterNats;

internal static class ResponsePublisher
{
    // To avoid boxing, cache generic type and invoke it.
    static readonly Func<Type, PublishResponseMessage> createPublisher = CreatePublisher;
    static readonly ConcurrentDictionary<Type, PublishResponseMessage> publisherCache = new();

    public static void PublishResponse(Type type, NatsOptions options, in NatsKey subject, in ReadOnlySequence<byte> buffer, object callback)
    {
        publisherCache.GetOrAdd(type, createPublisher).Invoke(options, subject, buffer, callback);
    }

    static PublishResponseMessage CreatePublisher(Type type)
    {
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

    public static void PublishRequest(Type requestType, Type responseType, NatsConnection connection,  in NatsKey replyTo, in ReadOnlySequence<byte> buffer, object callback)
    {
        publisherCache.GetOrAdd((requestType, responseType), createPublisher).Invoke(connection,replyTo, buffer, callback);
    }

    static PublishRequestMessage CreatePublisher((Type requestType, Type responseType) type)
    {
        var publisher = typeof(RequestPublisher<,>).MakeGenericType(type.requestType, type.responseType)!;
        var instance = Activator.CreateInstance(publisher)!;
        return (PublishRequestMessage)Delegate.CreateDelegate(typeof(PublishRequestMessage), instance, "Publish", false);
    }
}

internal delegate void PublishResponseMessage(NatsOptions options, in NatsKey subject, in ReadOnlySequence<byte> buffer, object callback);
internal delegate void PublishRequestMessage(NatsConnection connection, in NatsKey replyTo, in ReadOnlySequence<byte> buffer, object callback);

internal sealed class ResponsePublisher<T>
{
    public void Publish(NatsOptions options, in NatsKey subject, in ReadOnlySequence<byte> buffer, object callback)
    {
        // when empty(RequestPublisher detect exception)
        if (buffer.IsEmpty)
        {
            ((IPromise<T?>)callback).SetException(new NatsException("Request handler throws exception."));
            return;
        }

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
            if (!connection.Options.UseThreadPoolCallback)
            {
                if (callback is Func<TRequest, TResponse> func)
                {
                    PublishSync(connection, value, replyTo, func);
                }
                else if (callback is Func<TRequest, Task<TResponse>> asyncFunc)
                {
                    PublishAsync(connection, value, replyTo, asyncFunc);
                }
            }
            else
            {
                ThreadPool.UnsafeQueueUserWorkItem(static state =>
                {
                    var (connection, value, replyTo, callback) = state;
                    if (callback is Func<TRequest, TResponse> func)
                    {
                        PublishSync(connection, value, replyTo, func);
                    }
                    else if (callback is Func<TRequest, Task<TResponse>> asyncFunc)
                    {
                        PublishAsync(connection, value, replyTo, asyncFunc);
                    }
                }, (connection, value, replyTo, callback), false);
            }
        }
        catch (Exception ex)
        {
            try
            {
                connection.Options.LoggerFactory.CreateLogger<RequestPublisher<TRequest, TResponse>>().LogError(ex, "Error occured during request handler.");
            }
            catch { }
        }

        static void PublishSync(NatsConnection connection, TRequest? value, in NatsKey replyTo, Func<TRequest, TResponse> callback)
        {
            TResponse response = default!;
            try
            {
                response = callback.Invoke(value!);
            }
            catch (Exception ex)
            {
                try
                {
                    connection.Options.LoggerFactory.CreateLogger<RequestPublisher<TRequest, TResponse>>().LogError(ex, "Error occured during request handler.");
                }
                catch { }
                connection.PostPublish(replyTo); // send empty when error
                return;
            }

            connection.PostPublish(replyTo, response); // send response.
        }

        static async void PublishAsync(NatsConnection connection, TRequest? value, NatsKey replyTo, Func<TRequest, Task<TResponse>> callback)
        {
            TResponse response = default!;
            try
            {
                response = await callback.Invoke(value!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    connection.Options.LoggerFactory.CreateLogger<RequestPublisher<TRequest, TResponse>>().LogError(ex, "Error occured during request handler.");
                }
                catch { }
                connection.PostPublish(replyTo); // send empty when error
                return;
            }

            connection.PostPublish(replyTo, response); // send response.
        }
    }
}
