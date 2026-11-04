"""Validate that installation hostnames resolve directly to this server."""
import ipaddress
import json
import socket
import subprocess
import sys
import urllib.request
import uuid


def public_addresses(overrides):
    if overrides:
        addresses = {ipaddress.ip_address(value) for value in overrides}
        if not all(address.is_global for address in addresses):
            raise ValueError('--public-ip requires a public IPv4 or IPv6 address.')
        return addresses
    interfaces = json.loads(subprocess.check_output(['ip', '-j', 'address', 'show', 'scope', 'global']))
    addresses = {ipaddress.ip_address(info['local']) for interface in interfaces for info in interface['addr_info']}
    return {address for address in addresses if address.is_global}


def cloudflare_networks():
    with urllib.request.urlopen('https://api.cloudflare.com/client/v4/ips', timeout=10) as response:
        result = json.load(response)['result']
    return [ipaddress.ip_network(value) for value in result['ipv4_cidrs'] + result['ipv6_cidrs']]


def check_dns(domain, expected, proxy_networks):
    probe = f'automaton-dns-{uuid.uuid4().hex[:12]}'
    for prefix in ('api', 'studio', 'store', 'apps', f'{probe}.apps'):
        host = f'{prefix}.{domain}'
        record = f'*.apps.{domain}' if prefix.startswith(probe) else host
        try:
            answers = {ipaddress.ip_address(entry[4][0]) for entry in
                       socket.getaddrinfo(host + '.', None, type=socket.SOCK_STREAM)}
        except socket.gaierror as error:
            raise ValueError(f'{record}: could not resolve {host}. Add a DNS-only A/AAAA record pointing to this server.') from error
        if any(address in network for address in answers for network in proxy_networks):
            raise ValueError(f'{record}: resolves to a Cloudflare proxy. Set the record to DNS only (grey cloud).')
        if not answers or not answers <= expected:
            raise ValueError(f'{record}: points outside this server. Correct or remove any wrong A/AAAA records.')


def main(domain, overrides):
    try:
        expected = public_addresses(overrides)
        if not expected:
            raise ValueError('No public address on this server. For a server behind NAT, specify --public-ip ADDRESS.')
        check_dns(domain, expected, cloudflare_networks())
    except (OSError, ValueError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        raise SystemExit(f'DNS preflight failed: {error}') from error
    print('DNS preflight passed.')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2:])
