import ipaddress
from pathlib import Path
import socket
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'docker/install'))
from check_dns import check_dns


class InstallDnsTests(unittest.TestCase):
    expected = {ipaddress.ip_address('203.0.113.20')}

    def check(self, address_by_host):
        def resolve(host, *args, **kwargs):
            self.assertTrue(host.endswith('.'))
            address = address_by_host(host)
            if address is None:
                raise socket.gaierror(socket.EAI_NONAME)
            return [(None, None, None, None, (value, 0)) for value in address]

        with patch('socket.getaddrinfo', side_effect=resolve) as resolver:
            check_dns('example.com', self.expected)
            return [call.args[0] for call in resolver.call_args_list]

    def test_resolves_four_hosts_and_an_app_wildcard_sample(self):
        hosts = self.check(lambda _: ['203.0.113.20'])
        self.assertEqual(hosts[:4], [f'{name}.example.com.' for name in ('api', 'studio', 'store', 'apps')])
        self.assertRegex(hosts[4], r'^automaton-dns-[a-f0-9]{12}\.apps\.example\.com\.$')

    def test_missing_app_wildcard_names_the_record(self):
        with self.assertRaisesRegex(ValueError, r'\*\.apps\.example\.com: could not resolve'):
            self.check(lambda host: None if host.startswith('automaton-dns-') else ['203.0.113.20'])

    def test_mixed_proxy_and_origin_answers_fail(self):
        for proxy in ('104.16.0.1', '2606:4700::1'):
            with self.subTest(proxy=proxy), self.assertRaisesRegex(ValueError, 'store.example.com:.*DNS only'):
                self.check(lambda host: ['203.0.113.20', proxy] if host.startswith('store.') else ['203.0.113.20'])

    def test_stale_aaaa_fails_even_when_a_record_matches(self):
        with self.assertRaisesRegex(ValueError, 'api.example.com:.*wrong A/AAAA'):
            self.check(lambda _: ['203.0.113.20', '2001:db8::2'])


if __name__ == '__main__':
    unittest.main()
