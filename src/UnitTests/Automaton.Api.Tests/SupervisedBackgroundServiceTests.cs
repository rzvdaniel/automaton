using Automaton.Api.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class SupervisedBackgroundServiceTests
{
    private sealed class FlakyService(int[] runs) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (Interlocked.Increment(ref runs[0]) < 3) throw new TimeoutException("database timeout");
            return Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error) lock (Messages) Messages.Add(formatter(state, exception));
        }
    }

    [Test]
    public async Task Throwing_service_is_logged_by_name_and_restarted_without_stopping_the_host()
    {
        var runs = new int[1];
        var logger = new ListLogger<SupervisedBackgroundService<FlakyService>>();
        using var supervisor = new SupervisedBackgroundService<FlakyService>(
            () => new FlakyService(runs), logger, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));

        await supervisor.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref runs[0]) < 3 && DateTime.UtcNow < deadline) await Task.Delay(10);

        Assert.That(Volatile.Read(ref runs[0]), Is.EqualTo(3));
        Assert.That(supervisor.ExecuteTask!.IsFaulted, Is.False);
        Assert.That(logger.Messages.Count, Is.EqualTo(2));
        Assert.That(logger.Messages, Has.All.Contain("FlakyService"));
        await supervisor.StopAsync(CancellationToken.None);
    }
}
