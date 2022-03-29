using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AlterNats.Internal;

internal sealed class ThreadPoolWorkItem<T> : IThreadPoolWorkItem
{
    static readonly ConcurrentQueue<ThreadPoolWorkItem<T>> pool = new();

    ThreadPoolWorkItem<T>? nextNode;
    public ref ThreadPoolWorkItem<T>? NextNode => ref nextNode;

    Action<T?>? continuation;
    T? value;

    ThreadPoolWorkItem()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ThreadPoolWorkItem<T> Create(Action<T?> continuation, T? value)
    {
        if (!pool.TryDequeue(out var item))
        {
            item = new ThreadPoolWorkItem<T>();
        }

        item.continuation = continuation;
        item.value = value;
        return item;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute()
    {
        var call = continuation;
        var v = value;
        continuation = null;
        value = default;
        if (call != null)
        {
            pool.Enqueue(this);

            try
            {
                call.Invoke(v);
            }
            catch
            {
                // TODO: do nanika(logging?)
            }
        }
    }
}
