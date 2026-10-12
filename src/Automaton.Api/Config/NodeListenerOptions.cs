namespace Automaton.Api.Config;

/// <summary>Configured local ports for the isolated Node ingress listeners.</summary>
public sealed class NodeListenerOptions
{
    public const string SectionName = "NodeListeners";

    public int? ManagementPort { get; set; }
    public int? BootstrapPort { get; set; }
}
