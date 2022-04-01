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
    [MemberData(nameof(BasicTestData))]
    public async Task Basic<T>(int subPort, int pubPort, IEnumerable<T> items)
    {
        AutoResetEvent autoResetEvent = new AutoResetEvent(false);

        autoResetEvent.Reset();
        List<T> results = new();

        var natsKey = new NatsKey(Guid.NewGuid().ToString("N"));

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
    [MemberData(nameof(BasicTestData))]
    public async Task BasicRequest<T>(int subPort, int pubPort, IEnumerable<T> items)
    {
        var natsKey = new NatsKey(Guid.NewGuid().ToString("N"));

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

    static readonly int[] seed1 = { 24, 45, 99, 41, 98, 7, 81, 8, 26, 56 };
    static readonly int[] seed2 = { 86, 21, 30, 64, 97, 24, 58, 51, 12, 57 };

    static object[][] BasicTestData()
    {
        return new[]
        {
            new object[] { 4222, 4222, seed1 },
            new object[] { 4222, 4223, seed1 },
            new object[] { 4223, 4222, seed1 },
            new object[] { 4223, 4223, seed1 },
            new object[] { 4222, 4222, seed1.Select(x => $"Test:{x}") },
            new object[] { 4222, 4222, seed1.Select(x => new SampleClass(x, $"Name{x}")) }
        };
    }

    static object?[][] SubjectTestData()
    {
        return new[]
        {
            new object?[] { "subject", 99, 99, 99, null, null, null },
            new object?[] { "subject.a", 99, null, null, 99, 99, null },
            new object?[] { "subject.a.b", 99, null, null, null, 99, null },
            new object?[] { "other", 99, 99, null, null, null, 99 },
        };
    }

    [Theory]
    [MemberData(nameof(SubjectTestData))]
    public async Task SubjectTest(string pubKey, int? expect1, int? expect2, int? expect3, int? expect4, int? expect5, int? expect6)
    {
        AutoResetEvent autoResetEvent1 = new AutoResetEvent(false);
        AutoResetEvent autoResetEvent2 = new AutoResetEvent(false);
        AutoResetEvent autoResetEvent3 = new AutoResetEvent(false);
        AutoResetEvent autoResetEvent4 = new AutoResetEvent(false);
        AutoResetEvent autoResetEvent5 = new AutoResetEvent(false);
        AutoResetEvent autoResetEvent6 = new AutoResetEvent(false);

        int? result1 = null;
        int? result2 = null;
        int? result3 = null;
        int? result4 = null;
        int? result5 = null;
        int? result6 = null;

        await using var subConnection = new NatsConnection();

        await subConnection.ConnectAsync();

        using var d1 = await subConnection.SubscribeAsync<int>(">", x =>
        {
            result1 = x;
            autoResetEvent1.Set();
        });

        using var d2 = await subConnection.SubscribeAsync<int>("*", x =>
        {
            result2 = x;
            autoResetEvent2.Set();
        });

        using var d3 = await subConnection.SubscribeAsync<int>("subject", x =>
        {
            result3 = x;
            autoResetEvent3.Set();
        });

        using var d4 = await subConnection.SubscribeAsync<int>("subject.*", x =>
        {
            result4 = x;
            autoResetEvent4.Set();
        });

        using var d5 = await subConnection.SubscribeAsync<int>("subject.>", x =>
        {
            result5 = x;
            autoResetEvent5.Set();
        });

        using var d6 = await subConnection.SubscribeAsync<int>("other", x =>
        {
            result6 = x;
            autoResetEvent6.Set();
        });

        await using var pubConnection = new NatsConnection();

        await pubConnection.ConnectAsync();

        await pubConnection.PublishAsync(new NatsKey(pubKey), 99);

        WaitHandle.WaitAll(new WaitHandle[]
        {
            autoResetEvent1,
            autoResetEvent2,
            autoResetEvent3,
            autoResetEvent4,
            autoResetEvent5,
            autoResetEvent6
        }, 1000);

        Assert.Equal(expect1, result1);
        Assert.Equal(expect2, result2);
        Assert.Equal(expect3, result3);
        Assert.Equal(expect4, result4);
        Assert.Equal(expect5, result5);
        Assert.Equal(expect6, result6);
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
