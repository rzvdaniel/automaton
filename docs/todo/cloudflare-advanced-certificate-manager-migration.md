# Cloudflare Advanced Certificate Manager Migration

## Purpose

Automaton currently publishes apps on hostnames such as:

```text
https://space-invaders.apps.automaton.monster
```

The wildcard DNS record is:

```text
*.apps.automaton.monster
```

For now, this record is configured as **DNS only** in Cloudflare. This works because browsers connect directly to the Automaton Traefik origin, which already has a valid certificate covering `*.apps.automaton.monster`.

This document explains why a future migration to Cloudflare Advanced Certificate Manager is useful and how to perform it safely.

---

## Why the current DNS-only setup works

Cloudflare Universal SSL normally covers:

```text
automaton.monster
*.automaton.monster
```

It does not cover deeper hostnames such as:

```text
*.apps.automaton.monster
```

When the wildcard record was proxied, Cloudflare had to terminate the visitor TLS connection. Because its edge certificate did not cover the deeper wildcard, browsers failed during the Cloudflare TLS handshake.

Changing the record to **DNS only** bypassed Cloudflare's proxy:

```text
Visitor
→ DNS resolves directly to Automaton origin
→ Traefik presents the origin wildcard certificate
→ hosted app is served
```

This is a valid temporary configuration.

---

## Why migrate later

With the record set to DNS only, app traffic does not pass through Cloudflare's HTTP proxy. This means the app-hosting origin IP is directly visible and app traffic does not benefit from Cloudflare's Layer 7 services.

Migrating to Advanced Certificate Manager would allow `*.apps.automaton.monster` to be proxied safely while preserving the current app URL structure.

Benefits include:

- Cloudflare edge TLS coverage for deeper app subdomains.
- The Runner/application origin IP is hidden behind Cloudflare.
- Cloudflare DDoS mitigation applies to proxied app traffic.
- WAF, rate limiting, bot controls, caching and HTTP analytics can be used for app hostnames.
- TLS certificates are issued and renewed by Cloudflare.
- New app slugs continue to work without creating an individual DNS record.
- The existing `{app-slug}.apps.automaton.monster` routing design can remain unchanged.

---

## Recommended certificate strategy

After activating the paid Advanced Certificate Manager add-on, use one of these approaches.

### Preferred: explicit advanced wildcard certificate

Order an advanced certificate containing:

```text
automaton.monster
*.automaton.monster
*.apps.automaton.monster
```

The important hostname is:

```text
*.apps.automaton.monster
```

It covers all app hostnames at that level, including future apps.

Examples:

```text
space-invaders.apps.automaton.monster
iss-tracker.apps.automaton.monster
my-new-app.apps.automaton.monster
```

### Optional: Total TLS

Advanced Certificate Manager also unlocks Total TLS. Total TLS can automatically issue certificates for proxied hostnames not covered by Universal SSL.

For Automaton, an explicit `*.apps.automaton.monster` wildcard certificate is still preferable because it covers all current and future app slugs with one certificate. Total TLS may be enabled as additional protection for other deeper hostnames.

---

## Prerequisites

Before starting:

- The Automaton origin must continue serving HTTPS correctly.
- Traefik must have a valid certificate covering `*.apps.automaton.monster`.
- `space-invaders.apps.automaton.monster` or another test app must work while the wildcard record is DNS only.
- The Cloudflare zone must be active.
- A valid Cloudflare billing method must be available.
- Do not disable Universal SSL.

---

## Migration procedure

### 1. Activate Advanced Certificate Manager

In Cloudflare:

```text
automaton.monster
→ SSL/TLS
→ Edge Certificates
→ Advanced Certificate Manager
→ Activate
```

Review the price and billing confirmation before completing activation.

### 2. Order the advanced certificate

From **SSL/TLS → Edge Certificates**, choose **Order Advanced Certificate**.

Include:

```text
automaton.monster
*.automaton.monster
*.apps.automaton.monster
```

Use Cloudflare's recommended certificate authority and validation method unless Automaton has a specific reason to customize them.

### 3. Wait for the certificate to become active

Do not change the wildcard DNS record back to proxied yet.

Confirm that the new certificate status is:

```text
Active
```

Also inspect the certificate details and verify that its covered hostnames include:

```text
*.apps.automaton.monster
```

### 4. Test certificate coverage before the switch

Use Cloudflare's dashboard certificate details or an isolated test hostname to confirm the certificate exists.

A suitable test hostname is:

```text
certificate-test.apps.automaton.monster
```

It does not need to route to a real app for certificate inspection, but a real deployed app is better for the final test.

### 5. Switch the wildcard record to proxied

In **DNS → Records**, edit:

```text
Type: CNAME
Name: *.apps
Target: apps.automaton.monster
```

Change:

```text
DNS only
```

to:

```text
Proxied
```

Save the record.

### 6. Verify the public app

Test:

```text
https://space-invaders.apps.automaton.monster
```

Confirm:

- the browser reports a valid certificate;
- the page returns successfully;
- static resources load;
- backend requests work;
- no redirect loop occurs;
- no `ERR_SSL_VERSION_OR_CIPHER_MISMATCH` appears;
- no Cloudflare `502`, `521`, `522`, or `525` error appears.

### 7. Verify the origin connection mode

Cloudflare should use:

```text
SSL/TLS encryption mode: Full (strict)
```

This ensures both connections are encrypted and validated:

```text
Visitor
→ HTTPS → Cloudflare
→ HTTPS → Traefik origin
```

Do not use Flexible mode.

### 8. Run Automaton production checks

Verify:

- API remains healthy.
- Studio remains healthy.
- Website remains healthy.
- Existing app routes still resolve.
- A newly published app slug works without adding another DNS record.
- Hosted Runner runtime routes continue to use the correct Runner Host public IP and port.
- Cloudflare does not cache dynamic API responses incorrectly.

---

## Rollback procedure

If the migration causes a public outage:

1. Open Cloudflare DNS.
2. Edit `*.apps.automaton.monster`.
3. Change the record from **Proxied** back to **DNS only**.
4. Save.
5. Wait for DNS and local caches to update.
6. Retest the app directly against the Traefik origin.

Do not remove the Advanced Certificate immediately while diagnosing the problem. First restore service with DNS-only mode, then inspect certificate status, Cloudflare events and origin connectivity.

---

## Post-migration hardening

After the wildcard works through Cloudflare, consider:

- Keep SSL/TLS mode on **Full (strict)**.
- Enable appropriate WAF managed rules.
- Add rate limiting for public app API endpoints.
- Review cache rules so HTML and APIs are not cached incorrectly.
- Preserve WebSocket support for any applications that require it.
- Configure origin access so ports 80/443 accept traffic only from Cloudflare IP ranges, if operationally practical.
- Keep Runner Host runtime ports restricted to the Automaton Traefik/API server rather than exposing them broadly.
- Add monitoring for certificate status and app-route failures.
- Document the Cloudflare add-on cost and renewal ownership.

---

## Important distinction: edge certificate vs origin certificate

Two TLS connections exist when Cloudflare proxying is enabled:

```text
Visitor → Cloudflare
Cloudflare → Automaton origin
```

The Advanced Certificate covers the first connection. The Traefik/origin certificate covers the second.

The production incident occurred on the first connection: Cloudflare's edge certificate did not cover `*.apps.automaton.monster`. The origin certificate was already valid.

Both certificates should remain valid after the migration.

---

## Current and future states

### Current temporary state

```text
*.apps.automaton.monster: DNS only
Visitor → Traefik origin directly
Origin certificate handles TLS
```

### Intended future state

```text
*.apps.automaton.monster: Proxied
Visitor → Cloudflare edge → Traefik origin
Advanced certificate handles edge TLS
Origin certificate handles origin TLS
```

---

## Completion checklist

- [ ] Advanced Certificate Manager activated.
- [ ] Advanced certificate ordered.
- [ ] `*.apps.automaton.monster` included.
- [ ] Certificate status is Active.
- [ ] SSL/TLS mode is Full (strict).
- [ ] Wildcard DNS record changed to Proxied.
- [ ] Existing hosted app loads successfully.
- [ ] Frontend assets load.
- [ ] Backend/API calls work.
- [ ] New app slug works automatically.
- [ ] Production verification passes.
- [ ] Rollback procedure tested or documented for the operator.

---

## Official Cloudflare references

- Universal SSL limitations:  
  https://developers.cloudflare.com/ssl/edge-certificates/universal-ssl/limitations/

- Advanced Certificate Manager:  
  https://developers.cloudflare.com/ssl/edge-certificates/advanced-certificate-manager/

- Manage advanced certificates:  
  https://developers.cloudflare.com/ssl/edge-certificates/advanced-certificate-manager/manage-certificates/

- Total TLS:  
  https://developers.cloudflare.com/ssl/edge-certificates/additional-options/total-tls/

- Enable Total TLS:  
  https://developers.cloudflare.com/ssl/edge-certificates/additional-options/total-tls/enable/

- Cloudflare wildcard DNS records:  
  https://developers.cloudflare.com/dns/manage-dns-records/reference/wildcard-dns-records/

---

## Status

As of July 2026, Automaton intentionally remains on the temporary DNS-only configuration for:

```text
*.apps.automaton.monster
```

No application-code or Traefik-routing migration is required when Advanced Certificate Manager is introduced. The planned change is primarily:

```text
Activate ACM
→ issue certificate for *.apps.automaton.monster
→ verify Active
→ switch wildcard DNS back to Proxied
→ validate production
```
