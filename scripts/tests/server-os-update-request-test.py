#!/usr/bin/env python3
"""Exercise os-update-from-request.sh with a disposable directory and a fake apt-get; no package ever changes."""
import fcntl
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import time
import unittest

REPO = Path(__file__).resolve().parents[2]
SCRIPT = REPO / 'docker/install/os-update-from-request.sh'

# Simulates: "-s upgrade" lists two pending updates (one security) until an install/upgrade ran; then none.
FAKE_APT = r'''#!/usr/bin/env bash
state="$AUTOMATON_FAKE_DIR/done"
echo "$*" >> "$AUTOMATON_FAKE_DIR/calls"
if [[ "$1" == -s ]]; then
    if [[ ! -e "$state" ]]; then
        echo 'Inst openssl [1] (2 Ubuntu:24.04/noble-security [amd64])'
        echo 'Inst vim [1] (2 Ubuntu:24.04/noble-updates [amd64])'
    fi
    echo 'The following packages have been kept back:'
    echo '  linux-image-generic linux-headers-generic'
    echo '0 upgraded, 0 newly installed, 0 to remove and 2 not upgraded.'
    exit 0
fi
[[ -e "$AUTOMATON_FAKE_FAIL" ]] && { echo 'E: could not get lock'; exit 100; }
echo "apt ran: $*"
echo 'Unpacking openssl (2) over (1) ...'
[[ -n "$AUTOMATON_FAKE_SLOW" ]] && sleep 2
echo 'Unpacking vim (2) over (1) ...'
echo 'Setting up vim (2) ...'
touch "$state"
'''


@unittest.skipUnless(shutil.which('flock') and shutil.which('python3'), 'Requires flock and python3.')
class OsUpdateFromRequestTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='automaton-os-update-')
        self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name)
        self.os_dir, self.update, self.restart = root / 'os', root / 'update', root / 'restart'
        self.systemd, self.install, self.fake = root / 'systemd', root / 'install', root / 'fake'
        for directory in (self.systemd, self.install, self.fake):
            directory.mkdir()
        apt = self.fake / 'apt-get'
        apt.write_text(FAKE_APT)
        apt.chmod(apt.stat().st_mode | stat.S_IXUSR)
        self.backup_lock = root / 'backup.lock'
        self.reboot_required = root / 'reboot-required'
        self.fail_marker = self.fake / 'fail'
        self.environment = os.environ | {
            'AUTOMATON_OS_UPDATE_TEST': '1', 'AUTOMATON_OS_UPDATE_DIR': str(self.os_dir), 'AUTOMATON_UPDATE_DIR': str(self.update),
            'AUTOMATON_RESTART_DIR': str(self.restart), 'AUTOMATON_SYSTEMD_DIR': str(self.systemd),
            'AUTOMATON_BACKUP_LOCK_FILE': str(self.backup_lock), 'AUTOMATON_OS_UPDATE_APT': str(apt),
            'AUTOMATON_REBOOT_REQUIRED_FILE': str(self.reboot_required),
            'AUTOMATON_FAKE_DIR': str(self.fake), 'AUTOMATON_FAKE_FAIL': str(self.fail_marker),
        }
        self.run_script('--install-units')

    def run_script(self, *arguments):
        return subprocess.run(['bash', str(SCRIPT), '--install-dir', str(self.install), *arguments],
                              env=self.environment, capture_output=True, text=True)

    def request(self, content):
        path = self.os_dir / 'requests/request.json'
        path.write_text(content if isinstance(content, str) else json.dumps(content))
        return path

    def status(self):
        return json.loads((self.os_dir / 'status/status.json').read_text())

    def calls(self):
        path = self.fake / 'calls'
        return path.read_text().splitlines() if path.exists() else []

    def test_installs_root_only_request_directory_and_units(self):
        self.assertEqual((self.os_dir / 'requests').stat().st_mode & 0o777, 0o700)
        self.assertIn('PathExists=', (self.systemd / 'automaton-os-update.path').read_text())
        self.assertIn('Type=oneshot', (self.systemd / 'automaton-os-update.service').read_text())

    def test_security_scope_upgrades_only_the_security_packages_and_keeps_config_files(self):
        path = self.request({'requestedBy': 'admin@example.com 1234', 'scope': 'security'})
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(path.exists())
        install = [call for call in self.calls() if 'install' in call]
        self.assertEqual(len(install), 1)
        self.assertIn('--force-confold', install[0])
        self.assertIn('--only-upgrade install openssl', install[0])
        self.assertNotIn('vim', install[0])
        status = self.status()
        self.assertEqual((status['state'], status['scope'], status['upgradedCount']), ('succeeded', 'security', 2))
        self.assertEqual(status['requestedBy'], 'admin@example.com 1234')
        self.assertFalse(status['restartRequired'])
        self.assertIn('apt ran', status['logTail'])

    def test_all_scope_runs_a_full_upgrade_and_reports_a_needed_restart(self):
        self.reboot_required.write_text('*** System restart required ***')
        self.request({'requestedBy': 'x', 'scope': 'all'})
        self.assertEqual(self.run_script().returncode, 0)
        upgrade = [call for call in self.calls() if call.endswith(' upgrade') and not call.startswith('-s')]
        self.assertEqual(len(upgrade), 1)
        self.assertIn('-o Dpkg::Options::=--force-confold', upgrade[0])
        self.assertTrue(self.status()['restartRequired'])

    def test_all_scope_reports_total_progress_and_the_packages_apt_holds_back(self):
        self.request({'requestedBy': 'x', 'scope': 'all'})
        self.assertEqual(self.run_script().returncode, 0)
        status = self.status()
        self.assertEqual((status['totalCount'], status['progressCount']), (2, 2))
        self.assertIsNone(status['currentPackage'])
        self.assertEqual(status['heldBack'], ['linux-image-generic', 'linux-headers-generic'])

    def test_status_shows_progress_and_the_current_package_while_apt_runs(self):
        self.environment['AUTOMATON_FAKE_SLOW'] = '1'
        self.environment['AUTOMATON_OS_UPDATE_POLL'] = '0.5'
        self.request({'requestedBy': 'x', 'scope': 'all'})
        process = subprocess.Popen(['bash', str(SCRIPT), '--install-dir', str(self.install)], env=self.environment,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        seen = None
        for _ in range(40):
            time.sleep(0.2)
            try:
                status = self.status()
            except (OSError, ValueError):
                continue
            if status['state'] == 'running' and status.get('progressCount'):
                seen = status
                break
        process.communicate(timeout=30)
        self.assertIsNotNone(seen, 'no running status with progress was written')
        self.assertEqual(seen['totalCount'], 2)
        self.assertIn('Unpacking', seen['currentPackage'])
        self.assertIsNone(seen['heldBack'])

    def test_a_failed_apt_run_is_reported_with_its_log(self):
        self.fail_marker.write_text('')
        self.request({'requestedBy': 'x', 'scope': 'all'})
        self.run_script()
        status = self.status()
        self.assertEqual(status['state'], 'failed')
        self.assertIn('could not get lock', status['logTail'])
        self.assertEqual(status['errorLine'], 'E: could not get lock')

    def test_refuses_while_a_backup_holds_its_lock(self):
        self.backup_lock.write_text('')
        with open(self.backup_lock) as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            path = self.request({'requestedBy': 'x'})
            result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(path.exists())
        self.assertEqual([call for call in self.calls() if 'install' in call], [])
        self.assertEqual(self.status()['state'], 'refused')
        self.assertIn('backup is running', self.status()['message'])

    def test_refuses_while_a_server_update_runs_or_the_server_is_rebooting(self):
        (self.update / 'status').mkdir(parents=True)
        (self.update / 'status/status.json').write_text('{"state": "running"}')
        self.request({'requestedBy': 'x'})
        self.assertNotEqual(self.run_script().returncode, 0)
        self.assertIn('update is running', self.status()['message'])
        (self.update / 'status/status.json').write_text('{"state": "succeeded"}')
        (self.restart / 'status').mkdir(parents=True)
        (self.restart / 'status/status.json').write_text('{"state": "rebooting"}')
        self.request({'requestedBy': 'x'})
        self.assertNotEqual(self.run_script().returncode, 0)
        self.assertIn('restarting', self.status()['message'])
        self.assertEqual([call for call in self.calls() if 'install' in call], [])

    def test_rejects_malformed_requests_and_never_runs_apt(self):
        for content in ('not json', '[]', json.dumps({'scope': 'dist-upgrade'}), json.dumps({'scope': 5})):
            path = self.request(content)
            self.assertNotEqual(self.run_script().returncode, 0, content)
            self.assertFalse(path.exists())
            self.assertEqual(self.calls(), [], content)
            self.assertEqual(self.status()['state'], 'refused')

    def test_a_request_that_is_not_a_regular_file_is_removed_without_following_it(self):
        target = Path(self.temporary.name) / 'secret.txt'
        target.write_text('{"requestedBy": "x"}')
        (self.os_dir / 'requests/request.json').symlink_to(target)
        self.assertNotEqual(self.run_script().returncode, 0)
        self.assertEqual(self.calls(), [])
        self.assertTrue(target.exists())

    def test_the_requester_name_is_never_executed(self):
        self.request({'requestedBy': '$(touch pwned); `touch pwned`'})
        self.run_script()
        self.assertEqual(self.status()['requestedBy'], 'unknown')
        self.assertFalse((Path.cwd() / 'pwned').exists())

    def test_the_restart_script_refuses_while_an_os_update_holds_its_lock(self):
        (self.os_dir).mkdir(exist_ok=True)
        lock_path = self.os_dir / '.lock'
        lock_path.write_text('')
        restart_script = REPO / 'docker/install/restart-from-request.sh'
        environment = self.environment | {'AUTOMATON_RESTART_TEST': '1'}
        subprocess.run(['bash', str(restart_script), '--install-dir', str(self.install), '--install-units'], env=environment, check=True, capture_output=True)
        (self.restart / 'requests/request.json').write_text('{"requestedBy": "x"}')
        with open(lock_path) as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            subprocess.run(['bash', str(restart_script), '--install-dir', str(self.install)], env=environment, capture_output=True)
        self.assertFalse((self.restart / 'reboot-called').exists())
        self.assertIn('OS update is running', json.loads((self.restart / 'status/status.json').read_text())['message'])


if __name__ == '__main__':
    unittest.main()
