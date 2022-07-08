using System.Runtime.CompilerServices;

namespace AlterNats.Internal;

// Pool and Node
internal sealed class CancellationTimerPool : IObjectPoolNode<CancellationTimerPool>
{
    CancellationTimerPool? next;
    public ref CancellationTimerPool? NextNode => ref next;

    readonly CancellationTokenSource cancellationTokenSource;

    public CancellationToken Token => cancellationTokenSource.Token;

    // this timer pool is tightly coupled with rootToken lifetime(e.g. connection lifetime).
    public CancellationTimerPool(CancellationToken rootToken)
    {
        this.cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(rootToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CancellationTimerPool Rent(ObjectPool pool, CancellationToken rootToken)
    {
        if (pool.TryRent<CancellationTimerPool>(out var self))
        {
            return self;
        }
        else
        {
            return new CancellationTimerPool(rootToken);
        }
    }

    public bool TryReturn(ObjectPool pool)
    {
        if (cancellationTokenSource.TryReset())
        {
            pool.Return(this);
            return true;
        }
        else
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
            return false;
        }
    }

    public void CancelAfter(TimeSpan delay)
    {
        cancellationTokenSource.CancelAfter(delay);
    }
}
