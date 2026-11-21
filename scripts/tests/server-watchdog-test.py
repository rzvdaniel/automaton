#!/usr/bin/env python3
"""The watchdog alerts (webhook) when containers or volumes are missing or the backup failed or is stale, starts missing
containers again only while every volume exists, and says nothing while all is well. Docker is a fake on PATH."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import unittest
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

REPO = Path(__file__).resolve().parents[2]
SCRIPT = REPO / 'docker/install/watchdog.py'
SERVICES = ['postgres', 'rabbitmq', 'api', 'worker', 'studio', 'traefik']
VOLUMES = ['postgres-data', 'rabbitmq-data', 'api-data', 'api-keys', 'worker-keys', 'certificates']

FAKE_DOCKER = r'''#!/usr/bin/env bash
d="$FAKE_DIR"
case "$1" in
  ps) while read -r s st; do echo "$s $st"; done < "$d/containers" ;;
  volume) cat "$d/volumes" ;;
  compose)
    case " $* " in
      *" config --services"*) tr ' ' '\n' <<< "$SERVICES" ;;
      *" config --volumes"*) tr ' ' '\n' <<< "$VOLUMES" ;;
      *" up "*) echo up >> "$d/up-calls"; [[ -e "$d/up-fails" ]] || { for s in $SERVICES; do echo "$s running"; done > "$d/containers"; } ;;
    esac ;;
esac
'''


class Receiver(BaseHTTPRequestHandler):
    messages = []

    def do_POST(self):
        Receiver.messages.append(json.loads(self.rfile.read(int(self.headers['Content-Length']))))
        self.send_response(200)
        self.end_headers()

    def log_message(self, *args):
        pass


class WatchdogTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.fake = root / 'fake'
        self.install = root / 'install'
        self.config = root / 'config'
        for d in (self.fake, self.install, self.config / 'status', root / 'bin', root / 'state'):
            d.mkdir(parents=True)
        docker = root / 'bin/docker'
        docker.write_text(FAKE_DOCKER)
        docker.chmod(0o755)
        (self.install / '.env').write_text(f'CONFIG_DIR={self.config}\n')
        (self.install / 'release.env').write_text('')
        (self.install / 'compose.yaml').write_text('')
        (self.config / 'appsettings.Production.json').write_text(json.dumps({'AppConfig': {'ApiUrl': ''}}))
        self.set_containers(SERVICES)
        self.set_volumes(VOLUMES)
        self.set_backup()
        Receiver.messages = []
        self.server = HTTPServer(('127.0.0.1', 0), Receiver)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        (self.config / 'alerts.json').write_text(json.dumps({'webhookUrl': f'http://127.0.0.1:{self.server.server_port}/hook'}))
        self.root = root

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.tmp.cleanup()

    def set_containers(self, running, stopped=()):
        (self.fake / 'containers').write_text(''.join(f'{s} running\n' for s in running) + ''.join(f'{s} exited\n' for s in stopped))

    def set_volumes(self, names):
        (self.fake / 'volumes').write_text(''.join(f'{n}\n' for n in names))

    def set_backup(self, hours_old=1, error=None):
        taken = (datetime.now(timezone.utc) - timedelta(hours=hours_old)).strftime('%Y-%m-%dT%H:%M:%SZ')
        (self.config / 'status/backup.json').write_text(json.dumps({'lastSuccessAt': taken, 'lastError': error}))

    def run_watchdog(self):
        env = dict(os.environ, PATH=f'{self.root / "bin"}:{os.environ["PATH"]}', FAKE_DIR=str(self.fake),
                   SERVICES=' '.join(SERVICES), VOLUMES=' '.join(VOLUMES), AUTOMATON_WATCHDOG_ALLOW_USER='1',
                   AUTOMATON_INSTALL_LOCK_FILE=str(self.root / 'no-install.lock'), AUTOMATON_BACKUP_LOCK_FILE=str(self.root / 'no-backup.lock'))
        result = subprocess.run(['python3', str(SCRIPT), '--install-dir', str(self.install), '--state-dir', str(self.root / 'state')],
                              capture_output=True, text=True, env=env)
        self.last = result.stdout + result.stderr
        return result.returncode

    def texts(self):
        return [m['text'] for m in Receiver.messages]

    def test_healthy_server_sends_nothing(self):
        self.assertEqual(self.run_watchdog(), 0)
        self.assertEqual(self.texts(), [], self.last)

    def test_stopped_api_is_started_again_and_reported_once(self):
        self.set_containers([s for s in SERVICES if s != 'api'], stopped=['api'])
        self.run_watchdog()
        self.assertEqual(len(self.texts()), 1)
        self.assertIn('Automaton is down: api stopped', self.texts()[0])
        self.assertIn('it is back', self.texts()[0])
        self.assertTrue((self.fake / 'up-calls').exists())
        self.assertEqual(self.run_watchdog(), 0)
        self.assertEqual(len(self.texts()), 1)

    def test_missing_volume_alerts_with_restore_command_and_never_starts_containers(self):
        self.set_volumes([v for v in VOLUMES if v != 'postgres-data'])
        self.set_containers([])
        (self.install / 'backups/nightly/automaton-20261120T030000Z').mkdir(parents=True)
        self.run_watchdog()
        text = '\n'.join(self.texts())
        self.assertIn("data volumes are missing (postgres-data)", text)
        self.assertIn('restore.sh automaton-20261120T030000Z --confirm', text)
        self.assertFalse((self.fake / 'up-calls').exists())

    def test_failed_backup_and_stale_backup(self):
        self.set_backup(error='The last backup failed. Run: sudo journalctl -u automaton-backup.service')
        self.run_watchdog()
        self.assertIn('Last backup failed', self.texts()[0])
        self.set_backup(hours_old=30)
        self.run_watchdog()
        self.assertIn('Last backup is 30 h old', self.texts()[-1])

    def test_same_problem_is_not_repeated_and_recovery_is_reported(self):
        self.set_backup(hours_old=30)
        self.run_watchdog()
        self.run_watchdog()
        self.assertEqual(len(self.texts()), 1)
        self.set_backup(hours_old=1)
        self.run_watchdog()
        self.assertEqual(len(self.texts()), 2)
        self.assertIn('recovered', self.texts()[1])

    def test_install_units(self):
        units = self.root / 'units'
        units.mkdir()
        env = dict(os.environ, AUTOMATON_SYSTEMD_DIR=str(units), AUTOMATON_WATCHDOG_ALLOW_USER='1')
        subprocess.run(['python3', str(SCRIPT), '--install-units', '--install-dir', '/opt/x'], check=True, env=env, capture_output=True)
        self.assertIn('OnUnitActiveSec=5min', (units / 'automaton-watchdog.timer').read_text())
        self.assertIn('watchdog.py --install-dir /opt/x', (units / 'automaton-watchdog.service').read_text())


if __name__ == '__main__':
    unittest.main()
