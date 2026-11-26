#!/usr/bin/env python3
"""Static checks for the unattended (cloud-init) installer mode; starts nothing."""
import re
import subprocess
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / 'scripts/install-automaton.sh'
TEXT = SCRIPT.read_text()


class UnattendedInstallerTests(unittest.TestCase):
    def test_syntax(self):
        subprocess.run(['bash', '-n', str(SCRIPT)], check=True)

    def test_needs_domain_email_and_admin_email_and_logs_to_root_file(self):
        self.assertIn("--unattended needs --domain, --email and --admin-email", TEXT)
        self.assertIn("install -m 0600 /dev/null /var/log/automaton-install.log", TEXT)

    def test_no_prompt_and_dns_retries_in_unattended_mode(self):
        self.assertIn('if $unattended; then\n        # The records are often added', TEXT)
        self.assertIn('until python3 "$bundle_dir/check_dns.py"', TEXT)

    def test_admin_link_helper_compiles_and_never_prints_a_secret(self):
        code = re.search(r"<<'PYLINK'\n(.*?)\nPYLINK", TEXT, re.S).group(1)
        compile(code, 'admin-link.py', 'exec')
        self.assertNotRegex(code, r'print\(')
        self.assertIn('0o600', code)

    def test_runner_helper_compiles_and_never_prints_the_command(self):
        code = re.search(r"<<'PYRUN'\n(.*?)\nPYRUN", TEXT, re.S).group(1)
        compile(code, 'connect-runner.py', 'exec')
        self.assertNotRegex(code, r'print\([^)]*installationCommand')
        self.assertIn('stdout=subprocess.DEVNULL', code)
        self.assertIn('--no-runner) connect_runner=false', TEXT)
        self.assertIn('if [[ -f "$link_file" ]] && $connect_runner; then', TEXT)


if __name__ == '__main__':
    unittest.main()
