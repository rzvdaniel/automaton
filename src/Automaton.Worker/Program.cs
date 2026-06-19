using Automaton.Worker.Consumers;
using Automaton.Worker.Handlers;
using Automaton.Messaging.Abstractions;
using DotNetCore.CAP;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTransient<UserRegisteredCapSubscriber>();
builder.Services.AddTransient<IEventHandler<Automaton.Messaging.Events.UserRegisteredEvent>, UserRegisteredEventHandler>();
builder.Services.AddCap(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("MySqlConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:MySqlConnection is required.");
    var rabbitMqHost = builder.Configuration["RabbitMq:Host"]
        ?? throw new InvalidOperationException("RabbitMq:Host is required.");
    var rabbitMqUsername = builder.Configuration["RabbitMq:Username"]
        ?? throw new InvalidOperationException("RabbitMq:Username is required.");
    var rabbitMqPassword = builder.Configuration["RabbitMq:Password"]
        ?? throw new InvalidOperationException("RabbitMq:Password is required.");

    options.UseMySql(connectionString);
    options.UseRabbitMQ(rabbitMq =>
    {
        rabbitMq.HostName = rabbitMqHost;
        rabbitMq.UserName = rabbitMqUsername;
        rabbitMq.Password = rabbitMqPassword;
        rabbitMq.VirtualHost = builder.Configuration["RabbitMq:VirtualHost"] ?? "/";
    });
});

var host = builder.Build();
host.Run();
