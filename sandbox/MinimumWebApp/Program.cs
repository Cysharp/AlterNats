using AlterNats;

var builder = WebApplication.CreateBuilder(args);

// Register NatsConnectionPool, NatsConnection, INatsCommand to ServiceCollection
builder.Services.AddNats();

var app = builder.Build();

app.MapGet("/subscribe", (INatsCommand command) => command.SubscribeAsync("foo", (NatsKey s,int x) => Console.WriteLine($"received {x} on subject {s}")));
app.MapGet("/publish", (INatsCommand command) => command.PublishAsync("foo", 99));

app.Run();
