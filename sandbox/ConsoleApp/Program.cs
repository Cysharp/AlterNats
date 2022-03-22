
using AlterNats;
using MessagePack;
using System.Reflection;
using System.Text;






await using var conn = new NatsConnection();

//Console.WriteLine("GO PING");
var t = Foo();
//var b = conn.PingAsync();
//var c = conn.PingAsync();
//var d = conn.PingAsync();
//var e = conn.PingAsync();
//// await a;
//await b;
//await c;
//await d;
//await e;
//Console.WriteLine("END PING");


Console.ReadLine();

await t;

Console.ReadLine();


async Task Foo()
{
    await conn.PingAsync();
    throw new Exception("YEAH?");
}