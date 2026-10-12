namespace Automaton.Api.Config;

/// <summary>Trust settings for the TLS client certificate presented by the Node ingress proxy.</summary>
public sealed class NodeProxyAuthenticationOptions
{
    public const string SectionName = "NodeProxyAuthentication";
    public const string RequiredUriSan = "urn:automaton:traefik:node-ingress";

    public string TrustedRootCertificateBase64 { get; set; } = string.Empty;
    public string[] AllowedLeafSha256Fingerprints { get; set; } = Array.Empty<string>();
}
