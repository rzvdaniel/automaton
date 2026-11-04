#!/usr/bin/env python3
"""Exercise update-from-request.sh with a disposable update directory and a fake update.sh."""
import fcntl
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
SCRIPT = REPO / 'docker/install/update-from-request.sh'

FAKE_UPDATER = '''#!/usr/bin/env bash
echo "$@" > "$FAKE_ARGS"
echo "Creating protected recovery backup at /opt/test/backups/server-v0.2.0-backup"
echo "login password=hunter2 and token: abc123"
[[ -z "${FAKE_LINES:-}" ]] || seq 1 "$FAKE_LINES"
if [[ "${FAKE_FAIL:-}" == 1 ]]; then echo 'Update did not reach healthy state.' >&2; exit 3; fi
echo server-v0.2.0 > "$(dirname "$0")/version"
'''


@unittest.skipUnless(shutil.which('flock') and shutil.which('python3'), 'Requires flock and python3.')
class UpdateFromRequestTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='automaton-update-request-')
        self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name)
        self.install = root / 'install'
        self.update = root / 'update'
        self.systemd = root / 'systemd'
        for directory in (self.install, self.systemd):
            directory.mkdir()
        (self.install / 'version').write_text('server-v0.1.1\n')
        (self.install / 'update.sh').write_text(FAKE_UPDATER)
        self.args = root / 'args.txt'
        self.environment = os.environ | {
            'AUTOMATON_UPDATE_TEST': '1', 'AUTOMATON_UPDATE_DIR': str(self.update),
            'AUTOMATON_SYSTEMD_DIR': str(self.systemd), 'FAKE_ARGS': str(self.args),
        }
        self.run_script('--install-units')

    def run_script(self, *arguments, extra=None):
        return subprocess.run(['bash', str(SCRIPT), '--install-dir', str(self.install), *arguments],
                              env=self.environment | (extra or {}), capture_output=True, text=True)

    def request(self, content):
        path = self.update / 'requests/request.json'
        path.write_text(content if isinstance(content, str) else json.dumps(content))
        return path

    def status(self):
        return json.loads((self.update / 'status/status.json').read_text())

    def test_installs_root_only_request_directory_and_units(self):
        self.assertEqual(stat.S_IMODE((self.update / 'requests').stat().st_mode), 0o700)
        self.assertEqual(stat.S_IMODE((self.update / 'status').stat().st_mode), 0o755)
        path_unit = (self.systemd / 'automaton-update.path').read_text()
        self.assertIn(f'PathExists={self.update}/requests/request.json', path_unit)
        self.assertIn('Unit=automaton-update.service', path_unit)
        service = (self.systemd / 'automaton-update.service').read_text()
        self.assertIn('Type=oneshot', service)
        self.assertIn(f'update-from-request.sh --install-dir {self.install}', service)

    def test_runs_the_installed_updater_for_latest_and_reports_success(self):
        request = self.request({'version': 'latest', 'requestedBy': 'admin@example.com 11111111'})
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(request.exists())
        self.assertEqual(self.args.read_text().split(), ['--install-dir', str(self.install)])
        status = self.status()
        self.assertEqual(status['state'], 'succeeded')
        self.assertEqual(status['requestedVersion'], 'latest')
        self.assertEqual(status['fromVersion'], 'server-v0.1.1')
        self.assertEqual(status['toVersion'], 'server-v0.2.0')
        self.assertEqual(status['exitCode'], 0)
        self.assertEqual(status['recoveryBackup'], '/opt/test/backups/server-v0.2.0-backup')
        self.assertEqual(status['requestedBy'], 'admin@example.com 11111111')
        self.assertTrue(status['startedAtUtc'] and status['finishedAtUtc'])

    def test_passes_a_requested_version_to_the_updater(self):
        self.request({'version': 'server-v0.2.0'})
        self.assertEqual(self.run_script().returncode, 0)
        self.assertEqual(self.args.read_text().split(), ['--install-dir', str(self.install), '--version', 'server-v0.2.0'])
        self.assertEqual(self.status()['requestedVersion'], 'server-v0.2.0')

    def test_failed_update_reports_exit_code_log_and_backup(self):
        self.request({'version': 'latest'})
        self.assertNotEqual(self.run_script(extra={'FAKE_FAIL': '1'}).returncode, 0)
        status = self.status()
        self.assertEqual(status['state'], 'failed')
        self.assertEqual(status['exitCode'], 3)
        self.assertEqual(status['toVersion'], 'server-v0.1.1')
        self.assertEqual(status['recoveryBackup'], '/opt/test/backups/server-v0.2.0-backup')
        self.assertIn('Update did not reach healthy state.', (self.update / 'status/update.log').read_text())

    def test_log_is_trimmed_and_never_echoes_secrets(self):
        self.request({'version': 'latest'})
        self.run_script(extra={'FAKE_LINES': '500'})
        log = (self.update / 'status/update.log').read_text()
        self.assertLessEqual(len(log.splitlines()), 200)
        self.assertNotIn('hunter2', log)
        self.assertNotIn('abc123', log)
        self.request({'version': 'latest'})
        self.run_script()
        log = (self.update / 'status/update.log').read_text()
        self.assertIn('password=***', log)
        self.assertNotIn('hunter2', log)
        self.assertNotIn('abc123', log)

    def test_rejects_anything_but_latest_or_a_release_tag_without_running_the_updater(self):
        for content in ({'version': 'server-v0.2.0; touch /tmp/pwned'}, {'version': '$(id)'}, {'version': 'server-v1.2.3-beta.1'},
                        {'version': 'server-v1.2'}, {'version': ['latest']}, {'version': 7}, ['latest'], 'not json', '', '{"version":'):
            request = self.request(content)
            result = self.run_script()
            self.assertEqual(result.returncode, 1, (content, result.stdout + result.stderr))
            self.assertFalse(request.exists(), content)
            self.assertFalse(self.args.exists(), content)
            self.assertEqual(self.status()['state'], 'failed', content)
            self.assertEqual(self.status()['exitCode'], 1)

    def test_rejects_an_oversized_request(self):
        self.request({'version': 'latest', 'padding': 'x' * 5000})
        self.assertEqual(self.run_script().returncode, 1)
        self.assertFalse(self.args.exists())

    def test_unsafe_requested_by_is_replaced_not_used(self):
        self.request({'version': 'latest', 'requestedBy': '$(id); rm -rf /'})
        self.assertEqual(self.run_script().returncode, 0)
        self.assertEqual(self.status()['requestedBy'], 'unknown')

    def test_symlink_request_is_refused(self):
        target = Path(self.temporary.name) / 'elsewhere.json'
        target.write_text('{"version":"latest"}')
        (self.update / 'requests/request.json').symlink_to(target)
        self.assertNotEqual(self.run_script().returncode, 0)
        self.assertFalse(self.args.exists())
        self.assertTrue(target.exists())

    def test_second_request_while_an_update_holds_the_lock_is_refused(self):
        request = self.request({'version': 'latest'})
        with open(self.update / '.lock', 'w') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            result = self.run_script()
        self.assertEqual(result.returncode, 0)
        self.assertIn('already running', result.stderr)
        self.assertFalse(request.exists())
        self.assertFalse(self.args.exists())

    def test_no_request_does_nothing(self):
        self.assertEqual(self.run_script().returncode, 0)
        self.assertFalse((self.update / 'status/status.json').exists())

    def test_compose_mounts_requests_writable_and_status_read_only_into_the_api(self):
        compose = (REPO / 'docker/install/compose.yaml').read_text()
        self.assertIn('- /var/lib/automaton/update/requests:/app/update-requests\n', compose)
        self.assertIn('- /var/lib/automaton/update/status:/app/update-status:ro\n', compose)


if __name__ == '__main__':
    unittest.main()
