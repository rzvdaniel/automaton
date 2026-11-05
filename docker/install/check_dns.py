"""Validate that installation hostnames resolve directly to this server."""
import ipaddress
import json
import socket
import subprocess
import sys
import uuid


# Published at https://www.cloudflare.com/ips/. An unknown proxy address still
# fails the origin comparison, so this list only controls the diagnostic.
CLOUDFLARE_NETWORKS = tuple(map(ipaddress.ip_network, (
    '173.245.48.0/20',
    '103.21.244.0/22',
    '103.22.200.0/22',
    '103.31.4.0/22',
    '141.101.64.0/18',
    '108.162.192.0/18',
    '190.93.240.0/20',
    '188.114.96.0/20',
    '197.234.240.0/22',
    '198.41.128.0/17',
    '162.158.0.0/15',
    '104.16.0.0/13',
    '104.24.0.0/14',
    '172.64.0.0/13',
    '131.0.72.0/22',
    '2400:cb00::/32',
    '2606:4700::/32',
    '2803:f800::/32',
    '2405:b500::/32',
    '2405:8100::/32',
    '2a06:98c0::/29',
    '2c0f:f248::/32',
)))


def is_public_unicast(address):
    return address.is_global and not address.is_multicast


def public_addresses(overrides):
    if overrides:
        addresses = {ipaddress.ip_address(value) for value in overrides}
        if not all(map(is_public_unicast, addresses)):
            raise ValueError('--public-ip requires a public IPv4 or IPv6 address.')
        return addresses
    interfaces = json.loads(subprocess.check_output(['ip', '-j', 'address', 'show', 'scope', 'global']))
    addresses = {ipaddress.ip_address(info['local']) for interface in interfaces for info in interface['addr_info'] if 'local' in info}
    return {address for address in addresses if is_public_unicast(address)}


def check_dns(domain, expected):
    probe = f'automaton-dns-{uuid.uuid4().hex[:12]}'
    for prefix in ('api', 'studio', 'store', 'apps', f'{probe}.apps'):
        host = f'{prefix}.{domain}'
        record = f'*.apps.{domain}' if prefix.startswith(probe) else host
        try:
            answers = {ipaddress.ip_address(entry[4][0]) for entry in
                       socket.getaddrinfo(host + '.', None, type=socket.SOCK_STREAM)}
        except socket.gaierror as error:
            raise ValueError(f'{record}: could not resolve {host}. Add a DNS-only A/AAAA record pointing to this server.') from error
        if any(address in network for address in answers for network in CLOUDFLARE_NETWORKS):
            raise ValueError(f'{record}: resolves to a Cloudflare proxy. Set the record to DNS only (grey cloud).')
        if not answers or not answers <= expected:
            raise ValueError(f'{record}: points outside this server. Correct or remove any wrong A/AAAA records.')


def main(domain, overrides):
    try:
        expected = public_addresses(overrides)
        if not expected:
            raise ValueError('No public address on this server. For a server behind NAT, specify --public-ip ADDRESS.')
        check_dns(domain, expected)
    except (OSError, ValueError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        raise SystemExit(f'DNS preflight failed: {error}') from error
    print('DNS preflight passed.')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2:])
