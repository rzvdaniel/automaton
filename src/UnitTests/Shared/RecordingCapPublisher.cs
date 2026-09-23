using DotNetCore.CAP;

namespace Automaton.Messaging.Tests;

public sealed class RecordingCapPublisher : ICapPublisher
{
    private readonly IServiceProvider? serviceProvider;

    public RecordingCapPublisher(IServiceProvider? serviceProvider = null)
    {
        this.serviceProvider = serviceProvider;
    }

    public List<(string Topic, object? Message, CancellationToken CancellationToken)> Messages { get; } = [];
    public IServiceProvider ServiceProvider => serviceProvider ?? throw new NotSupportedException();
    public ICapTransaction? Transaction { get; set; }

    public Task PublishAsync<T>(string name, T? contentObj, string? callbackName = null, CancellationToken cancellationToken = default)
    {
        Messages.Add((name, contentObj, cancellationToken));
        return Task.CompletedTask;
    }

    public Task PublishAsync<T>(string name, T? contentObj, IDictionary<string, string?> headers, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public void Publish<T>(string name, T? contentObj, string? callbackName = null)
        => throw new NotSupportedException();

    public void Publish<T>(string name, T? contentObj, IDictionary<string, string?> headers)
        => throw new NotSupportedException();

    public Task PublishDelayAsync<T>(TimeSpan delayTime, string name, T? contentObj, IDictionary<string, string?> headers, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task PublishDelayAsync<T>(TimeSpan delayTime, string name, T? contentObj, string? callbackName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public void PublishDelay<T>(TimeSpan delayTime, string name, T? contentObj, IDictionary<string, string?> headers)
        => throw new NotSupportedException();

    public void PublishDelay<T>(TimeSpan delayTime, string name, T? contentObj, string? callbackName = null)
        => throw new NotSupportedException();
}
