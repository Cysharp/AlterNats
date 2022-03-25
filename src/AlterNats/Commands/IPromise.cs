namespace AlterNats.Commands;

internal interface IPromise
{
    void SetResult();
    void SetCanceled(CancellationToken cancellationToken);
    void SetException(Exception exception);
}
