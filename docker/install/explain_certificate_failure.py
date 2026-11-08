#!/usr/bin/env python3
"""Turn Traefik ACME diagnostics into safe, actionable installer output."""
import re
import sys


def explain(logs, domain):
    acme = [line for line in logs.splitlines() if 'acme' in line.lower() or 'certificate' in line.lower()]
    text = '\n'.join(acme).lower()
    retry = re.search(r'retry after ([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2} utc)', text)
    if 'ratelimited' in text or 'rate limit' in text or 'too many certificates' in text:
        when = f' Retry after {retry.group(1).upper()}.' if retry else ' Wait for the limit to reset.'
        return ("Let's Encrypt refused a certificate because its issuance limit was reached." + when +
                f' Reinstalling with the same host names will not help before then. Use another host name, or after the limit resets rerun the installer: it restarts Traefik so it requests the certificates again.')
    if 'dns problem' in text or 'nxdomain' in text or 'no valid a records' in text:
        return (f"Let's Encrypt could not resolve the installation host names under {domain}. "
                'Point their public DNS records directly to this server, wait for DNS to propagate, then rerun the installer.')
    if 'connection refused' in text or 'connection timed out' in text or 'timeout during connect' in text:
        return ("Let's Encrypt could not reach this server to validate the host names. "
                'Check public DNS and allow inbound TCP 80 and 443 through the firewall, then rerun the installer.')
    return ("HTTPS certificate issuance or validation did not complete. Check that public DNS points to this server "
            'and inbound TCP 80 and 443 are open. Check Traefik logs for the ACME error, then rerun the installer after fixing it.')


if __name__ == '__main__':
    print(explain(sys.stdin.read(), sys.argv[1]), file=sys.stderr)
