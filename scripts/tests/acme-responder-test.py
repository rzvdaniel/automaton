#!/usr/bin/env python3
"""The challenge-only DNS responder (#891): answers its zone, refuses everything else, rate-limits."""
import importlib.util
import json
from pathlib import Path
import struct
import sys
import unittest

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('acme_responder', REPO / 'docker/install/acme_responder.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
ZONE = '_acme-challenge.apps.example.com'


def query(name, qtype, flags=0x0100):
    encoded = b''.join(bytes([len(x)]) + x.encode() for x in name.split('.')) + b'\x00'
    return struct.pack('!HHHHHH', 7, flags, 1, 0, 0, 0) + encoded + struct.pack('!HH', qtype, 1)


def header(response):
    ident, flags, _, answers = struct.unpack('!HHHH', response[:8])
    return ident, flags, answers


class ResponderTests(unittest.TestCase):
    def setUp(self):
        self.responder = module.Responder(ZONE, 'api.example.com')

    def test_txt_value_is_served_until_cleaned_up(self):
        self.assertTrue(self.responder.set_value(ZONE + '.', 'token-value', True))
        ident, flags, answers = header(self.responder.handle(query(ZONE, 16), '1.1.1.1'))
        self.assertEqual((ident, flags & 0xF, flags & 0x0400, answers), (7, 0, 0x0400, 1))  # NOERROR, authoritative
        self.assertIn(b'token-value', self.responder.handle(query(ZONE, 16), '1.1.1.1'))
        self.responder.set_value(ZONE, 'token-value', False)
        self.assertEqual(header(self.responder.handle(query(ZONE, 16), '1.1.1.1'))[2], 0)

    def test_ns_and_soa_for_the_zone(self):
        self.assertEqual(header(self.responder.handle(query(ZONE, 2), '1.1.1.1'))[2], 1)
        self.assertEqual(header(self.responder.handle(query(ZONE, 6), '1.1.1.1'))[2], 1)

    def test_other_names_are_refused_no_recursion(self):
        for name in ('example.com', 'google.com', 'apps.example.com', '_acme-challenge.apps.example.com.evil.com'):
            flags = header(self.responder.handle(query(name, 1), '1.1.1.1'))[1]
            self.assertEqual(flags & 0xF, 5, name)  # REFUSED
            self.assertEqual(flags & 0x0080, 0, name)  # recursion not available

    def test_only_the_zone_name_can_be_set(self):
        self.assertFalse(self.responder.set_value('_acme-challenge.other.example.com', 'x', True))

    def test_responses_and_garbage_are_ignored(self):
        self.assertIsNone(self.responder.handle(query(ZONE, 16, flags=0x8100), '1.1.1.1'))
        self.assertIsNone(self.responder.handle(b'\x00', '1.1.1.1'))

    def test_rate_limit_per_source(self):
        responder = module.Responder(ZONE, 'api.example.com', rate=0, burst=3)
        results = [responder.handle(query(ZONE, 16), '9.9.9.9') for _ in range(5)]
        self.assertEqual([r is not None for r in results], [True, True, True, False, False])
        self.assertIsNotNone(responder.handle(query(ZONE, 16), '8.8.8.8'))

    def test_oversized_udp_answer_is_truncated(self):
        for i in range(10):
            self.responder.set_value(ZONE, 'v' * 200 + str(i), True)
        flags = header(self.responder.handle(query(ZONE, 16), '1.1.1.1', limit=512))[1]
        self.assertTrue(flags & 0x0200)

    def test_control_port_requires_the_shared_secret(self):
        import base64
        good = 'Basic ' + base64.b64encode(b'acme:s3cret').decode()
        self.assertTrue(module.authorized(good, 's3cret'))
        self.assertFalse(module.authorized('Basic ' + base64.b64encode(b'acme:wrong').decode(), 's3cret'))
        self.assertFalse(module.authorized(None, 's3cret'))
        self.assertFalse(module.authorized('Basic !!!', 's3cret'))
        self.assertFalse(module.authorized(good, ''))  # no secret configured: nothing is authorized


if __name__ == '__main__':
    unittest.main()
