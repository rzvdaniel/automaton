import importlib.util
import os
from pathlib import Path
import subprocess
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

    @unittest.skipUnless(os.name == 'posix', 'Installer shell integration runs on Linux CI')
    def test_installer_failure_prints_diagnosis_as_last_lines(self):
        installer = (source.parents[2] / 'scripts/install-automaton.sh').read_text()
        function = installer.split('explain_https_failure() {', 1)[1].split('\n}', 1)[0]
        shell = '''compose=(fake_compose)
domain=example.com
bundle_dir="%s"
certificate_attempt_started=2026-11-07T12:00:00Z
fake_compose() {
  [[ "$*" == "logs --no-color --since 2026-11-07T12:00:00Z --tail 300 traefik" ]] || exit 7
  echo 'ACME 429 rateLimited: too many certificates (5) already issued; retry after 2026-11-08 13:26:14 UTC'
}
explain_https_failure() {%s
}
explain_https_failure https://api.example.com/health
''' % (source.parent.as_posix(), function)
        result = subprocess.run(['bash', '-c', shell], capture_output=True, text=True)
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn('HTTPS check failed', result.stderr)
        self.assertIn('2026-11-08 13:26:14 UTC', result.stderr.splitlines()[-1])


if __name__ == '__main__':
    unittest.main()
