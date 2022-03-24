using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Sources;

namespace AlterNats.Commands;

// TODO:lightweight PING requires more lightweight design.

// TODO:fail on send, setexception.
internal sealed class PingCommand : CommandBase<PingCommand>, IValueTaskSource, IPromise, IThreadPoolWorkItem
{
    ManualResetValueTaskSourceCore<object> core;

    // waiter for PONG. If error on Ping(Canceled/Exception), remove from this queue.
    List<PingCommand>? queue;

    PingCommand()
    {
    }

    public static PingCommand Create(List<PingCommand> queue)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PingCommand();
        }

        result.queue = queue;

        return result;
    }

    public override string WriteTraceMessage => "Write PING Command to buffer.";

    public override void Write(ProtocolWriter writer)
    {
        if (queue != null)
        {
            lock (queue)
            {
                queue.Add(this);
            }
        }

        writer.WritePing();
    }

    public ValueTask AsValueTask()
    {
        return new ValueTask(this, core.Version);
    }

    public override void Return()
    {
        // don't return manually, only allows from await.
    }

    public void SetResult()
    {
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        if (queue != null)
        {
            lock (queue)
            {
                queue.Remove(this);
            }
        }

        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(new OperationCanceledException(state.cancellationToken));
        }, (self: this, cancellationToken), preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        if (queue != null)
        {
            lock (queue)
            {
                queue.Remove(this);
            }
        }

        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(state.exception);
        }, (self: this, exception), preferLocal: false);
    }

    void IValueTaskSource.GetResult(short token)
    {
        core.GetResult(token);
        core.Reset();
        queue = null;
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


internal sealed class LightPingCommand : CommandBase<LightPingCommand>
{
    LightPingCommand()
    {
    }

    public static LightPingCommand Create()
    {
        if (!pool.TryPop(out var result))
        {
            result = new LightPingCommand();
        }
        return result;
    }

    public override string WriteTraceMessage => "Write PING Command to buffer.";

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePing();
    }
}
