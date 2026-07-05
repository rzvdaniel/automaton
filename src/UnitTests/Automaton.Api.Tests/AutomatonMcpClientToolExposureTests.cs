using Automaton.Ai;
using Automaton.Ai.Models;
using Automaton.Ai.Providers;
using Automaton.AiGuidance;
using Automaton.Api.Data;
using Automaton.Api.Services.Mcp;
using Automaton.Api.Services;
using Automaton.Mcp.Client;
using Automaton.Mcp.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public class AutomatonMcpClientToolExposureTests
{
    [Test]
    public async Task StartConversationAsync_ExposesAutomatonGuidanceTools_ToProviderTools()
    {
        await using var dbContext = CreateDbContext();
        var runtimeProvider = await CreateRuntimeProviderAsync(dbContext);
        var fakeProvider = new FakeAiProvider();
        var registry = new FakeAiProviderRegistry(fakeProvider);
        var toolProvider = new AppMcpToolProvider(
            NullLogger<AppMcpToolProvider>.Instance,
            [new AutomatonGuidanceTool(runtimeProvider)]);

        var client = new AutomatonMcpClient(
            NullLogger<AutomatonMcpClient>.Instance,
            registry,
            toolProvider);

        var request = new ConversationRequest
        {
            ConversationId = Guid.NewGuid().ToString(),
            Provider = fakeProvider.ProviderName,
            InitialMessage = "Help me fix this published app.",
            SystemPrompt = "System prompt",
            Mode = "autonomous",
            SupportsTools = true
        };

        var result = await client.StartConversationAsync(request);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(fakeProvider.ReceivedToolNames, Does.Contain(AutomatonGuidanceTool.ListGuidanceName));
        Assert.That(fakeProvider.ReceivedToolNames, Does.Contain(AutomatonGuidanceTool.GetGuidanceName));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<AiGuidanceRuntimeService> CreateRuntimeProviderAsync(ApplicationDbContext dbContext)
    {
        var service = new AiGuidanceRuntimeService(
            dbContext,
            new EmbeddedAutomatonGuidanceProvider(),
            NullLogger<AiGuidanceRuntimeService>.Instance);
        await service.SeedMissingDefaultsAsync();
        return service;
    }

    private sealed class FakeAiProvider : IAiProvider
    {
        public string ProviderName => "FakeProvider";
        public string DefaultModel => "fake-model";
        public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

        public List<string> ReceivedToolNames { get; } = [];

        public Task<ProviderResult> CallAsync(
            ConversationRequest request,
            IEnumerable<ProviderTool> tools,
            Func<string, string, string, Task<string>> executeToolAsync,
            CancellationToken cancellationToken)
        {
            ReceivedToolNames.AddRange(tools.Select(tool => tool.Name));
            return Task.FromResult(new ProviderResult("ok", 1));
        }
    }

    private sealed class FakeAiProviderRegistry : IAiProviderRegistry
    {
        private readonly IReadOnlyDictionary<string, IAiProvider> _providers;

        public FakeAiProviderRegistry(IAiProvider provider)
        {
            _providers = new Dictionary<string, IAiProvider>(StringComparer.OrdinalIgnoreCase)
            {
                [provider.ProviderName] = provider
            };
        }

        public IReadOnlyDictionary<string, IAiProvider> Providers => _providers;

        public IAiProvider? GetProvider(string providerName) =>
            _providers.TryGetValue(providerName, out var provider) ? provider : null;

        public bool IsImplemented(string providerName) => _providers.ContainsKey(providerName);

        public IEnumerable<string> GetImplementedProviderNames() => _providers.Keys;
    }
}
