using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AlterNats.Internal;

internal interface IObjectPoolNode<T>
{
    ref T? NextNode { get; }
}

// mutable struct, don't mark readonly.
[StructLayout(LayoutKind.Auto)]
internal struct ObjectPool<T>
    where T : class, IObjectPoolNode<T>
{
    int gate;
    int size;
    T? root;

    public int Size => size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPop([NotNullWhen(true)] out T result)
    {
        if (Interlocked.CompareExchange(ref gate, 1, 0) == 0)
        {
            var v = root;
            if (!(v is null))
            {
                ref var nextNode = ref v.NextNode;
                root = nextNode;
                nextNode = null;
                size--;
                result = v;
                Volatile.Write(ref gate, 0);
                return true;
            }

            Volatile.Write(ref gate, 0);
        }

        result = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPush(T item)
    {
        if (Interlocked.CompareExchange(ref gate, 1, 0) == 0)
        {
            item.NextNode = root;
            root = item;
            size++;
            Volatile.Write(ref gate, 0);
            return true;
        }
        return false;
    }
}