using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlterNats.Tests;

public class AppendableSequenceTest
{
    [Fact]
    public void Test()
    {
        var buffer1 = new byte[] { 1, 10, 20 };

        var a = new AppendableSequence();
        a.Append(buffer1);

        var seq = a.AsReadOnlySequence();
        seq.IsSingleSegment.ShouldBeTrue();
        seq.Length.Should().Be(3);
        seq.First.ShouldEqual(new byte[] { 1, 10, 20 });

        a.Append(new byte[] { 100, 150, 200, 240 });

        seq = a.AsReadOnlySequence();
        seq.IsSingleSegment.ShouldBeFalse();
        seq.Length.Should().Be(7);

        var i = 0;
        foreach (var item in seq)
        {
            if (i == 0)
            {
                item.ShouldEqual(new byte[] { 1, 10, 20 });
            }
            else if (i == 1)
            {
                item.ShouldEqual(new byte[] { 100, 150, 200, 240 });
            }
            else
            {
                throw new Exception();
            }

            i++;
        }

        a.Append(new byte[] { 99, 98, 97, 96, 95 });
        seq = a.AsReadOnlySequence();
        seq.IsSingleSegment.ShouldBeFalse();
        seq.Length.Should().Be(12);

        i = 0;
        foreach (var item in seq)
        {
            if (i == 0)
            {
                item.ShouldEqual(new byte[] { 1, 10, 20 });
            }
            else if (i == 1)
            {
                item.ShouldEqual(new byte[] { 100, 150, 200, 240 });
            }
            else if (i == 2)
            {
                item.ShouldEqual(new byte[] { 99, 98, 97, 96, 95 });
            }
            else
            {
                throw new Exception();
            }

            i++;
        }

        // finally slice check.
        EnumerateAll(seq.Slice(2)).ShouldEqual(new byte[] { 20, 100, 150, 200, 240, 99, 98, 97, 96, 95 });
        EnumerateAll(seq.Slice(5)).ShouldEqual(new byte[] { 200, 240, 99, 98, 97, 96, 95 });
        EnumerateAll(seq.Slice(8)).ShouldEqual(new byte[] { 98, 97, 96, 95 });
    }

    IEnumerable<byte> EnumerateAll(ReadOnlySequence<byte> seq)
    {
        foreach (var item in seq)
        {
            foreach (var item2 in item.ToArray())
            {
                yield return item2;
            }
        }
    }
}