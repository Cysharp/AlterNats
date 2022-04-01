using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AlterNats.Tests;

public class PubSubTest
{
    [Theory]
    [MemberData(nameof(TestData))]
    public async Task Basic<T>(int subPort, int pubPort, IEnumerable<T> items)
    {
        AutoResetEvent autoResetEvent = new AutoResetEvent(false);

        autoResetEvent.Reset();
        List<T> results = new();

        var natsKey = new NatsKey(Guid.NewGuid().ToString());

        await using var subConnection = new NatsConnection(NatsOptions.Default with
        {
            Port = subPort
        });

        await subConnection.ConnectAsync();

        using var d = await subConnection.SubscribeAsync<T>(natsKey, x =>
        {
            results.Add(x);

            if (results.Count == items.Count())
                autoResetEvent.Set();
        });

        await using var pubConnection = new NatsConnection(NatsOptions.Default with
        {
            Port = pubPort
        });

        await pubConnection.ConnectAsync();

        foreach (var item in items)
        {
            await pubConnection.PublishAsync(natsKey, item);
        }

        var waitResult = autoResetEvent.WaitOne(3000);

        Assert.True(waitResult, "Timeout");
        Assert.Equal(items.ToArray(), results.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public async Task BasicRequest<T>(int subPort, int pubPort, IEnumerable<T> items)
    {
        var natsKey = new NatsKey(Guid.NewGuid().ToString());

        await using var subConnection = new NatsConnection(NatsOptions.Default with
        {
            Port = subPort
        });

        await subConnection.ConnectAsync();

        using var d = await subConnection.SubscribeRequestAsync<T, string>(natsKey, x => $"Re{x}");

        await using var pubConnection = new NatsConnection(NatsOptions.Default with
        {
            Port = pubPort
        });

        await pubConnection.ConnectAsync();

        foreach (var item in items)
        {
            Assert.Equal($"Re{item}", await pubConnection.RequestAsync<T, string>(natsKey, item));
        }
    }

    [Fact]
    public async Task ConnectionException()
    {
        var connection1 = new NatsConnection(NatsOptions.Default with
        {
            Port = 4229
        });

        await Assert.ThrowsAsync<SocketException>(async () => await connection1.ConnectAsync());
    }

    static readonly int[] seed = { 24, 45, 99, 41, 98, 7, 81, 8, 26, 56 };

    static object[][] TestData()
    {
        return new[]
        {
            new object[] { 4222, 4222, seed },
            new object[] { 4222, 4223, seed },
            new object[] { 4223, 4222, seed },
            new object[] { 4223, 4223, seed },
            new object[] { 4222, 4222, seed.Select(x => $"Test:{x}") },
            new object[] { 4222, 4222, seed.Select(x => new SampleClass(x, $"Name{x}")) }
        };
    }
}

public class SampleClass : IEquatable<SampleClass>
{
    public int Id { get; set; }
    public string Name { get; set; }

    public SampleClass(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public bool Equals(SampleClass? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Id == other.Id && Name == other.Name;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((SampleClass)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
    }

    public override string ToString()
    {
        return $"{Id}-{Name}";
    }
}
