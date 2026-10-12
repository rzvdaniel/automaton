namespace Automaton.Api.Tests;

[TestFixture]
public sealed class NodeProxyCertificateValidatorTests
{
    [Test]
    public void Current_traefik_chain_uri_eku_and_leaf_pin_are_required()
    {
        var now = DateTimeOffset.UtcNow;
        using var authority = NodeProxyAuthenticationTestMaterial.Create(now);
        using var leaf = authority.CreateTraefikLeaf(now);
        var validator = authority.CreateValidator(leaf);

        Assert.That(validator.IsTrustedPeer(leaf, now.UtcDateTime), Is.True);
    }

    [Test]
    public void Missing_proxy_certificate_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        using var authority = NodeProxyAuthenticationTestMaterial.Create(now);
        using var leaf = authority.CreateTraefikLeaf(now);
        var validator = authority.CreateValidator(leaf);

        Assert.That(validator.IsTrustedPeer(null, now.UtcDateTime), Is.False);
    }

    [Test]
    public void Foreign_proxy_chain_is_rejected_even_when_leaf_fingerprint_is_configured()
    {
        var now = DateTimeOffset.UtcNow;
        using var trustedAuthority = NodeProxyAuthenticationTestMaterial.Create(now);
        using var foreignAuthority = NodeProxyAuthenticationTestMaterial.Create(now);
        using var foreignLeaf = foreignAuthority.CreateTraefikLeaf(now);
        var validator = trustedAuthority.CreateValidator(foreignLeaf);

        Assert.That(validator.IsTrustedPeer(foreignLeaf, now.UtcDateTime), Is.False);
    }

    [Test]
    public void Retired_proxy_leaf_is_rejected_after_only_the_replacement_pin_remains()
    {
        var now = DateTimeOffset.UtcNow;
        using var authority = NodeProxyAuthenticationTestMaterial.Create(now);
        using var retiredLeaf = authority.CreateTraefikLeaf(now);
        using var replacementLeaf = authority.CreateTraefikLeaf(now);
        var validator = authority.CreateValidator(replacementLeaf);

        Assert.Multiple(() =>
        {
            Assert.That(validator.IsTrustedPeer(retiredLeaf, now.UtcDateTime), Is.False);
            Assert.That(validator.IsTrustedPeer(replacementLeaf, now.UtcDateTime), Is.True);
        });
    }

    [Test]
    public void Proxy_leaf_without_the_required_uri_san_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        using var authority = NodeProxyAuthenticationTestMaterial.Create(now);
        using var leaf = authority.CreateTraefikLeaf(now, "urn:automaton:traefik:other-ingress");
        var validator = authority.CreateValidator(leaf);

        Assert.That(validator.IsTrustedPeer(leaf, now.UtcDateTime), Is.False);
    }

    [Test]
    public void Proxy_leaf_without_client_auth_eku_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        using var authority = NodeProxyAuthenticationTestMaterial.Create(now);
        using var leaf = authority.CreateTraefikLeaf(now, includeClientAuthenticationEku: false);
        var validator = authority.CreateValidator(leaf);

        Assert.That(validator.IsTrustedPeer(leaf, now.UtcDateTime), Is.False);
    }
}
