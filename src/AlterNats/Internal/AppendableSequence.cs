using System.Buffers;

namespace AlterNats.Internal;

// TODO: for reuse, add reset.
internal sealed class AppendableSequence
{
    Segment? first;
    Segment? last;
    int length;

    public ReadOnlySequence<byte> AsReadOnlySequence() => new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);

    public int Count => length;

    public AppendableSequence()
    {
    }

    public void Slice(long start)
    {
        // TODO:impl slice
        throw new NotImplementedException();
    }

    public void Append(ReadOnlyMemory<byte> buffer)
    {
        if (length == 0)
        {
            var first = new Segment(buffer);
            this.first = first;
            last = first;
            length = buffer.Length;
        }
        else
        {
            var newLast = new Segment(buffer);
            last!.SetNextSegment(newLast);
            newLast.SetRunningIndex(length);
            last = newLast;
            length += buffer.Length;
        }
    }

    // TODO:Segment should uses ObjectPool
    internal class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> buffer)
        {
            Memory = buffer;
        }

        internal void SetRunningIndex(long runningIndex)
        {
            this.RunningIndex = runningIndex;
        }

        internal void SetNextSegment(Segment? nextSegment)
        {
            this.Next = nextSegment;
        }
    }
}