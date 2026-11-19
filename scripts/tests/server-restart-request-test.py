#!/usr/bin/env python3
"""Exercise restart-from-request.sh with a disposable restart directory; nothing is ever rebooted."""
import datetime
import fcntl
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
SCRIPT = REPO / 'docker/install/restart-from-request.sh'


@unittest.skipUnless(shutil.which('flock') and shutil.which('python3'), 'Requires flock and python3.')
class RestartFromRequestTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='automaton-restart-request-')
        self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name)
        self.restart = root / 'restart'
        self.update = root / 'update'
        self.systemd = root / 'systemd'
        self.install = root / 'install'
        for directory in (self.systemd, self.install):
            directory.mkdir()
        self.backup_lock = root / 'backup.lock'
        self.environment = os.environ | {
            'AUTOMATON_RESTART_TEST': '1', 'AUTOMATON_RESTART_DIR': str(self.restart), 'AUTOMATON_UPDATE_DIR': str(self.update),
            'AUTOMATON_SYSTEMD_DIR': str(self.systemd), 'AUTOMATON_BACKUP_LOCK_FILE': str(self.backup_lock),
        }
        self.run_script('--install-units')

    def run_script(self, *arguments):
        return subprocess.run(['bash', str(SCRIPT), '--install-dir', str(self.install), *arguments],
                              env=self.environment, capture_output=True, text=True)

    def request(self, content):
        path = self.restart / 'requests/request.json'
        path.write_text(content if isinstance(content, str) else json.dumps(content))
        return path

    def status(self):
        return json.loads((self.restart / 'status/status.json').read_text())

    @staticmethod
    def in_seconds(seconds):
        return (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(seconds=seconds)).strftime('%Y-%m-%dT%H:%M:%SZ')

    def test_installs_root_only_request_directory_and_units(self):
        self.assertEqual((self.restart / 'requests').stat().st_mode & 0o777, 0o700)
        self.assertIn('PathExists=', (self.systemd / 'automaton-restart.path').read_text())
        self.assertIn('Type=oneshot', (self.systemd / 'automaton-restart.service').read_text())

    def test_restarts_now_and_consumes_the_request(self):
        path = self.request({'requestedBy': 'admin@example.com 1234'})
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(path.exists())
        self.assertTrue((self.restart / 'reboot-called').exists())
        self.assertEqual(self.status()['state'], 'rebooting')
        self.assertEqual(self.status()['requestedBy'], 'admin@example.com 1234')

    def test_schedules_a_restart_at_the_requested_time_without_rebooting(self):
        self.request({'requestedBy': 'x', 'restartAtUtc': self.in_seconds(3600)})
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse((self.restart / 'reboot-called').exists())
        self.assertEqual(self.status()['state'], 'scheduled')
        self.assertTrue(self.status()['restartAtUtc'])
        self.assertTrue((self.restart / 'scheduled').read_text().startswith('delay=3'))

    def test_refuses_while_a_backup_holds_its_lock(self):
        self.backup_lock.write_text('')
        with open(self.backup_lock) as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            path = self.request({'requestedBy': 'x'})
            result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(path.exists())
        self.assertFalse((self.restart / 'reboot-called').exists())
        self.assertEqual(self.status()['state'], 'refused')
        self.assertIn('backup is running', self.status()['message'])

    def test_refuses_while_a_server_update_is_running(self):
        (self.update / 'status').mkdir(parents=True)
        (self.update / 'status/status.json').write_text('{"state": "running"}')
        self.request({'requestedBy': 'x'})
        self.assertNotEqual(self.run_script().returncode, 0)
        self.assertFalse((self.restart / 'reboot-called').exists())
        self.assertIn('update is running', self.status()['message'])

    def test_a_scheduled_restart_is_checked_again_when_it_fires(self):
        self.request({'requestedBy': 'x', 'restartAtUtc': self.in_seconds(3600)})
        self.run_script()
        self.backup_lock.write_text('')
        with open(self.backup_lock) as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            self.assertEqual(self.run_script('--reboot').returncode, 0, 'a refusal must not leave the transient unit failed')
        self.assertFalse((self.restart / 'reboot-called').exists())
        self.assertEqual(self.status()['state'], 'refused')
        self.assertEqual(self.run_script('--reboot').returncode, 0)
        self.assertTrue((self.restart / 'reboot-called').exists())

    def test_rejects_malformed_requests_and_never_reboots(self):
        for content in ('not json', '[]', json.dumps({'restartAtUtc': 'tomorrow'}),
                        json.dumps({'restartAtUtc': self.in_seconds(30 * 24 * 3600)}), json.dumps({'restartAtUtc': 5})):
            path = self.request(content)
            self.assertNotEqual(self.run_script().returncode, 0, content)
            self.assertFalse(path.exists())
            self.assertFalse((self.restart / 'reboot-called').exists(), content)
            self.assertEqual(self.status()['state'], 'refused')

    def test_a_request_that_is_not_a_regular_file_is_removed_without_following_it(self):
        target = Path(self.temporary.name) / 'secret.txt'
        target.write_text('{"requestedBy": "x"}')
        (self.restart / 'requests/request.json').symlink_to(target)
        self.assertNotEqual(self.run_script().returncode, 0)
        self.assertFalse((self.restart / 'reboot-called').exists())
        self.assertTrue(target.exists())

    def test_the_requester_name_is_never_executed(self):
        self.request({'requestedBy': '$(touch pwned); `touch pwned`'})
        self.run_script()
        self.assertEqual(self.status()['requestedBy'], 'unknown')
        self.assertFalse((Path.cwd() / 'pwned').exists())


if __name__ == '__main__':
    unittest.main()
