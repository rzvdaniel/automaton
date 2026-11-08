#!/usr/bin/env python3
"""Challenge-only DNS responder for Let's Encrypt DNS-01 delegation (issue #891).

Answers TXT, NS and SOA for one zone (_acme-challenge.apps.<domain>) and refuses every other name, so it is never
an open resolver. Traefik's httpreq DNS provider sets and clears the TXT values over an internal HTTP port.
Standard library only.
"""
import argparse
import json
import socket
import struct
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from socketserver import StreamRequestHandler, ThreadingTCPServer

TYPE_NS, TYPE_SOA, TYPE_TXT, TYPE_ANY = 2, 6, 16, 255
RCODE_NOERROR, RCODE_FORMERR, RCODE_REFUSED = 0, 1, 5


class Responder:
    def __init__(self, zone, ns_name, rate=20, burst=40):
        self.zone = zone.lower().rstrip('.')
        self.ns_name = ns_name.lower().rstrip('.')
        self.values = []
        self.lock = threading.Lock()
        self.rate, self.burst = rate, burst
        self.buckets = {}

    def set_value(self, fqdn, value, present):
        if fqdn.lower().rstrip('.') != self.zone:
            return False
        with self.lock:
            if present and value not in self.values:
                self.values = (self.values + [value])[-10:]
            elif not present and value in self.values:
                self.values.remove(value)
        return True

    def allow(self, source):
        now = time.monotonic()
        with self.lock:
            tokens, last = self.buckets.get(source, (self.burst, now))
            tokens = min(self.burst, tokens + (now - last) * self.rate)
            if len(self.buckets) > 10000:
                self.buckets.clear()
            self.buckets[source] = (tokens - 1, now)
            return tokens >= 1

    def handle(self, data, source, limit=None):
        """Return the response bytes for a query, or None to stay silent (dropped or malformed)."""
        if len(data) < 12 or not self.allow(source):
            return None
        ident, flags, qdcount = struct.unpack('!HHH', data[:6])
        if flags & 0x8000:  # a response, not a query: never answer (avoids reflection loops)
            return None
        opcode = (flags >> 11) & 0xF
        try:
            name, offset = self._name(data, 12)
            qtype, qclass = struct.unpack('!HH', data[offset:offset + 4])
        except (ValueError, struct.error, IndexError):
            return self._header(ident, flags, RCODE_FORMERR, 0, b'')
        question = data[12:offset + 4]
        in_zone = name == self.zone or name.endswith('.' + self.zone)
        if qdcount != 1 or opcode != 0 or qclass != 1 or not in_zone:
            return self._header(ident, flags, RCODE_REFUSED, 0, question)
        answers = []
        if name == self.zone:
            with self.lock:
                values = list(self.values)
            if qtype in (TYPE_TXT, TYPE_ANY):
                answers += [self._record(TYPE_TXT, 60, bytes([len(v)]) + v.encode()) for v in values if len(v) < 256]
            if qtype in (TYPE_NS, TYPE_ANY):
                answers.append(self._record(TYPE_NS, 3600, self._encode(self.ns_name)))
            if qtype in (TYPE_SOA, TYPE_ANY):
                answers.append(self._record(TYPE_SOA, 3600, self._soa()))
        response = self._header(ident, flags, RCODE_NOERROR, len(answers), question, authoritative=True) + b''.join(answers)
        if limit and len(response) > limit:  # UDP: signal truncation so the client retries over TCP
            return self._header(ident, flags, RCODE_NOERROR, 0, question, authoritative=True, truncated=True)
        return response

    def _soa(self):
        return (self._encode(self.ns_name) + self._encode('hostmaster.' + self.ns_name)
                + struct.pack('!IIIII', 1, 3600, 600, 86400, 60))

    @staticmethod
    def _header(ident, query_flags, rcode, answers, question, authoritative=False, truncated=False):
        flags = 0x8000 | (query_flags & 0x0100) | (0x0400 if authoritative else 0) | (0x0200 if truncated else 0) | rcode
        return struct.pack('!HHHHHH', ident, flags, 1 if question else 0, answers, 0, 0) + question

    @staticmethod
    def _record(rtype, ttl, rdata):
        return b'\xc0\x0c' + struct.pack('!HHIH', rtype, 1, ttl, len(rdata)) + rdata

    @staticmethod
    def _encode(name):
        return b''.join(bytes([len(label)]) + label.encode() for label in name.split('.')) + b'\x00'

    @staticmethod
    def _name(data, offset):
        labels = []
        while True:
            length = data[offset]
            offset += 1
            if length == 0:
                return '.'.join(labels).lower(), offset
            if length & 0xC0 or offset + length > len(data):
                raise ValueError('unsupported name')
            labels.append(data[offset:offset + length].decode('ascii'))
            offset += length


def serve_udp(responder, host, port):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((host, port))
    while True:
        data, source = sock.recvfrom(1024)
        response = responder.handle(data, source[0], limit=512)
        if response:
            sock.sendto(response, source)


def serve_tcp(responder, host, port):
    class Handler(StreamRequestHandler):
        def handle(self):
            self.request.settimeout(5)
            try:
                size = struct.unpack('!H', self.rfile.read(2))[0]
                response = responder.handle(self.rfile.read(size), self.client_address[0])
                if response:
                    self.wfile.write(struct.pack('!H', len(response)) + response)
            except (OSError, struct.error):
                pass
    ThreadingTCPServer.allow_reuse_address = True
    ThreadingTCPServer.daemon_threads = True
    ThreadingTCPServer((host, port), Handler).serve_forever()


def serve_control(responder, host, port):
    """Traefik's httpreq provider: POST /present and /cleanup with {"fqdn": ..., "value": ...}."""
    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            try:
                body = json.loads(self.rfile.read(min(int(self.headers.get('Content-Length', 0)), 4096)))
                ok = self.path in ('/present', '/cleanup') and responder.set_value(
                    str(body['fqdn']), str(body['value']), self.path == '/present')
            except (ValueError, KeyError, TypeError):
                ok = False
            self.send_response(200 if ok else 400)
            self.end_headers()

        def log_message(self, *args):
            pass
    ThreadingHTTPServer((host, port), Handler).serve_forever()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--zone', required=True, help='for example _acme-challenge.apps.example.com')
    parser.add_argument('--ns-name', required=True, help='the server name the NS record points to, e.g. api.example.com')
    parser.add_argument('--dns-port', type=int, default=53)
    parser.add_argument('--control-port', type=int, default=8053)
    args = parser.parse_args()
    responder = Responder(args.zone, args.ns_name)
    for target, port in ((serve_udp, args.dns_port), (serve_tcp, args.dns_port), (serve_control, args.control_port)):
        threading.Thread(target=target, args=(responder, '0.0.0.0', port), daemon=True).start()
    threading.Event().wait()


if __name__ == '__main__':
    main()
