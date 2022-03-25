using System.Threading.Tasks.Sources;

namespace AlterNats.Commands;

internal sealed class PublishCommand<T> : CommandBase<PublishCommand<T>>
{
    NatsKey? subject;
    T? value;
    INatsSerializer? serializer;

    PublishCommand()
    {
    }

    // TODO:reply-to

    public static PublishCommand<T> Create(string subject, T? value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishCommand<T>();
        }

        result.subject = new NatsKey(subject); // TODO:use specified overload.
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public static PublishCommand<T> Create(NatsKey subject, T? value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishCommand<T>();
        }

        result.subject = subject;
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public override string WriteTraceMessage => "Write PUB Command to buffer.";

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject!, null, value, serializer!);
    }

    public override void Return()
    {
        subject = null;
        value = default;
        serializer = null;
        base.Return();
    }
}




internal sealed class PublishAsyncCommand<T> : CommandBase<PublishAsyncCommand<T>>, IValueTaskSource, IPromise, IThreadPoolWorkItem
{
    NatsKey? subject;
    T? value;
    INatsSerializer? serializer;
    ManualResetValueTaskSourceCore<object> core;

    PublishAsyncCommand()
    {
    }

    // TODO:reply-to

    public static PublishAsyncCommand<T> Create(NatsKey subject, T? value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishAsyncCommand<T>();
        }

        result.subject = subject;
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public override string WriteTraceMessage => "Write PUB Command to buffer.";

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject!, null, value, serializer!);
    }

    public override void Return()
    {
    }

    public ValueTask AsValueTask()
    {
        return new ValueTask(this, core.Version);
    }

    public void SetResult()
    {
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(new OperationCanceledException(state.cancellationToken));
        }, (self: this, cancellationToken), preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(state.exception);
        }, (self: this, exception), preferLocal: false);
    }

    void IValueTaskSource.GetResult(short token)
    {
        core.GetResult(token);
        core.Reset();
        subject = null;
        value = default;
        serializer = null;
        pool.TryPush(this);
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    void IThreadPoolWorkItem.Execute()
    {
        core.SetResult(null!);
    }
}








internal sealed class PublishRawCommand : CommandBase<PublishRawCommand>
{
    NatsKey? subject;
    ReadOnlyMemory<byte> value;

    PublishRawCommand()
    {
    }

    // TODO:reply-to
    // TODO:ReadOnlyMemory<byte> overload

    public static PublishRawCommand Create(string subject, byte[] value)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishRawCommand();
        }

        result.subject = new NatsKey(subject); // TODO:use specified overload.
        result.value = value;

        return result;
    }

    public static PublishRawCommand Create(NatsKey subject, byte[] value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishRawCommand();
        }

        result.subject = subject;
        result.value = value;

        return result;
    }

    public override string WriteTraceMessage => "Write PUB Command to buffer.";

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject!, null, value.Span);
    }

    public override void Return()
    {
        subject = null;
        value = default;
        base.Return();
    }
}
