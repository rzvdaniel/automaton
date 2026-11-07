import importlib.util
from pathlib import Path
import unittest


source = Path(__file__).resolve().parents[2] / 'docker/install/explain_certificate_failure.py'
spec = importlib.util.spec_from_file_location('certificate_error', source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CertificateFailureTests(unittest.TestCase):
    def test_rate_limit_reports_retry_time_without_raw_log(self):
        logs = ('traefik | Unable to obtain ACME certificate for domains "api.example.com": '
                '429 rateLimited: too many certificates (5) already issued; '
                'retry after 2026-11-08 13:26:14 UTC; token=secret')
        message = module.explain(logs, 'example.com')
        self.assertIn('issuance limit', message)
        self.assertIn('2026-11-08 13:26:14 UTC', message)
        self.assertIn('same host names will not help', message)
        self.assertNotIn('secret', message)

    def test_dns_failure_gives_fix(self):
        message = module.explain('ACME: DNS problem: NXDOMAIN', 'example.com')
        self.assertIn('public DNS records', message)
        self.assertIn('rerun', message)

    def test_connect_failure_gives_fix(self):
        message = module.explain('ACME: connection refused', 'example.com')
        self.assertIn('TCP 80 and 443', message)

    def test_unknown_failure_points_to_logs(self):
        message = module.explain('other failure', 'example.com')
        self.assertIn('Traefik logs', message)


if __name__ == '__main__':
    unittest.main()
