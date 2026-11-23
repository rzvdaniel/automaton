"""Validate that installation hostnames resolve directly to this server."""
import ipaddress
import json
import socket
import struct
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


def ns_query(zone, resolver='1.1.1.1'):
    """Names the public DNS returns for the NS records of zone (one UDP query; stdlib only)."""
    ident = uuid.uuid4().int & 0xFFFF
    name = b''.join(bytes([len(label)]) + label.encode() for label in zone.split('.')) + b'\x00'
    query = struct.pack('!HHHHHH', ident, 0x0100, 1, 0, 0, 0) + name + struct.pack('!HH', 2, 1)
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.settimeout(5)
        sock.sendto(query, (resolver, 53))
        data = sock.recv(4096)
    answers = struct.unpack('!H', data[6:8])[0]
    offset = 12 + len(name) + 4

    def read_name(offset):
        labels, jumped, end = [], False, offset
        while True:
            length = data[offset]
            if length & 0xC0:
                pointer = struct.unpack('!H', data[offset:offset + 2])[0] & 0x3FFF
                end = end if jumped else offset + 2
                offset, jumped = pointer, True
            elif length == 0:
                return '.'.join(labels).lower(), (end if jumped else offset + 1)
            else:
                labels.append(data[offset + 1:offset + 1 + length].decode('ascii'))
                offset += 1 + length

    names = []
    for _ in range(answers):
        _, offset = read_name(offset)
        rtype, _, _, length = struct.unpack('!HHIH', data[offset:offset + 10])
        offset += 10
        if rtype == 2:
            names.append(read_name(offset)[0])
        offset += length
    return names


def delegation_record(domain):
    return f'_acme-challenge.apps.{domain}  NS  api.{domain}'


def locally_assigned(address):
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.bind((str(address), 0))
        return True
    except OSError:
        return False


def port_53_free(address=''):
    """True when UDP and TCP port 53 can be bound on address (all addresses when empty)."""
    for kind in (socket.SOCK_DGRAM, socket.SOCK_STREAM):
        with socket.socket(socket.AF_INET, kind) as sock:
            try:
                sock.bind((address or '0.0.0.0', 53))
            except OSError:
                return False
    return True


def probe_responder(domain):
    """Ask the responder over UDP and TCP at each address api.<domain> resolves to that is assigned to this server.
    This proves the responder is published on port 53 for both protocols. It is a local test: a firewall in front of
    the server is covered by the public NS lookup in check_delegation. Behind NAT there is no local address to test."""
    zone = f'_acme-challenge.apps.{domain}'
    name = b''.join(bytes([len(label)]) + label.encode() for label in zone.split('.')) + b'\x00'
    query = struct.pack('!HHHHHH', 0x4153, 0, 1, 0, 0, 0) + name + struct.pack('!HH', 6, 1)
    host = f'api.{domain}'
    try:
        addresses = sorted({e[4][0] for e in socket.getaddrinfo(host, 53, socket.AF_INET, socket.SOCK_STREAM)})
    except socket.gaierror as error:
        raise ValueError(f'{host} does not resolve: {error}') from error
    for address in [a for a in addresses if locally_assigned(a)]:
        for protocol in ('UDP', 'TCP'):
            try:
                if protocol == 'UDP':
                    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
                        sock.settimeout(5)
                        sock.sendto(query, (address, 53))
                        data = sock.recv(512)
                else:
                    with socket.create_connection((address, 53), timeout=5) as sock:
                        sock.sendall(struct.pack('!H', len(query)) + query)
                        data = sock.recv(512)[2:]
                ok = len(data) >= 12 and struct.unpack('!H', data[:2])[0] == 0x4153 and data[2] & 0x04 and data[7] >= 1
            except OSError:
                ok = False
            if not ok:
                raise ValueError(f'the DNS responder did not answer on {protocol} port 53 at {address} from this server. Check that nothing else holds port 53 and that Docker published it.')


def check_delegation(domain):
    zone = f'_acme-challenge.apps.{domain}'
    probe_responder(domain)
    try:
        names = ns_query(zone)
    except (OSError, struct.error, IndexError, ValueError) as error:
        raise ValueError(f'could not look up {zone}: {error}') from error
    if f'api.{domain}' not in names:
        raise ValueError(f'the DNS record is missing. Add this record at your DNS host, then run the command again:\n    {delegation_record(domain)}')


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
    if sys.argv[1] == '--delegation':  # check the one record of the delegated TLS mode
        try:
            check_delegation(sys.argv[2])
        except ValueError as error:
            raise SystemExit(f'Delegated TLS preflight failed: {error}') from error
        print('The delegation record was found.')
    elif sys.argv[1] == '--bind-address':  # the IPv4 address the DNS responder listens on (empty when none)
        # Only an address assigned to this server can be bound; behind NAT the responder listens on all addresses.
        print(next(iter(sorted(str(a) for a in public_addresses(sys.argv[2:]) if a.version == 4 and locally_assigned(a))), ''))
    elif sys.argv[1] == '--port-free':  # exit 1 when port 53 (UDP or TCP) is taken on the given address
        raise SystemExit(0 if port_53_free(sys.argv[2] if len(sys.argv) > 2 else '') else 1)
    else:
        main(sys.argv[1], sys.argv[2:])
