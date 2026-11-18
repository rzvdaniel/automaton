#!/usr/bin/env python3
"""fail2ban filters match scanner and failed sign-in lines of Traefik's JSON access log and never real traffic;
the installer script writes the jails, allowlist and logrotate files without overwriting the allowlist."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / 'docker/install/fail2ban'


def line(host, status, request_host, method, path):
    return json.dumps({'ClientAddr': f'{host}:5', 'ClientHost': host, 'DownstreamStatus': status, 'RequestHost': request_host,
                       'RequestMethod': method, 'RequestPath': path, 'time': '2026-11-18T10:46:00Z'}, separators=(',', ':'))


@unittest.skipUnless(shutil.which('fail2ban-regex'), 'fail2ban-regex is not installed')
class FilterTests(unittest.TestCase):
    def matches(self, filter_name, lines):
        with tempfile.TemporaryDirectory() as tmp:
            log = Path(tmp) / 'access.log'
            log.write_text('\n'.join(lines) + '\n')
            out = subprocess.run(['fail2ban-regex', str(log), str(SOURCE / 'filter.d' / f'{filter_name}.conf')],
                                 capture_output=True, text=True).stdout
        return int(out.split('Lines:')[1].split('matched')[0].split(',')[-1])

    def test_scanner_filter_matches_only_junk_404s(self):
        junk = [('studio.x.com', '/.env'), ('a.apps.x.com', '/wp-admin/setup.php'), ('a.apps.x.com', '/x/credentials.json'),
                ('a.apps.x.com', '/index.php?a=1'), ('studio.x.com', '/anything-unknown'), ('api.x.com', '/.git/config')]
        self.assertEqual(self.matches('automaton-scanner', [line('9.9.9.9', 404, h, 'GET', p) for h, p in junk]), len(junk))
        real = [(404, 'a.apps.x.com', 'GET', '/deep/link'), (200, 'a.apps.x.com', 'GET', '/deep/link/credentials'),
                (200, 'studio.x.com', 'GET', '/.env'), (404, 'studio.x.com', 'GET', '/.well-known/acme-challenge/t'),
                (404, 'studio.x.com', 'GET', '/api/apps/1'), (404, 'studio.x.com', 'GET', '/assets/old-chunk.js'),
                (101, 'api.x.com', 'GET', '/hubs/studio'), (200, 'a.apps.x.com', 'POST', '/upload'),
                (200, 'api.x.com', 'POST', '/api/apps/1/files')]
        self.assertEqual(self.matches('automaton-scanner', [line('8.8.8.8', s, h, m, p) for s, h, m, p in real]), 0)

    def test_signin_filter_matches_only_failed_sign_ins(self):
        failed = line('7.7.7.7', 401, 'studio.x.com', 'POST', '/api/Account/LoginUser')
        ok = [line('7.7.7.7', 200, 'studio.x.com', 'POST', '/api/Account/LoginUser'),
              line('7.7.7.7', 401, 'studio.x.com', 'GET', '/api/apps'), line('7.7.7.7', 401, 'a.apps.x.com', 'POST', '/login')]
        self.assertEqual(self.matches('automaton-signin', [failed, failed] + ok), 2)


class InstallScriptTests(unittest.TestCase):
    def test_writes_jails_allowlist_and_logrotate_and_keeps_owner_allowlist(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            install, etc, rotate, bin_dir = tmp / 'install', tmp / 'etc', tmp / 'rotate', tmp / 'bin'
            shutil.copytree(SOURCE, install / 'fail2ban')
            bin_dir.mkdir()
            volume = tmp / 'volume'
            volume.mkdir()
            (bin_dir / 'fail2ban-client').write_text('#!/bin/sh\n')
            (bin_dir / 'docker').write_text(f'#!/bin/sh\ncase "$2" in ls) echo automaton-server_certificates;; *) echo {volume};; esac\n')
            for stub in bin_dir.iterdir():
                stub.chmod(0o755)
            env = os.environ | {'PATH': f'{bin_dir}:{os.environ["PATH"]}', 'AUTOMATON_FAIL2BAN_ETC': str(etc),
                                'AUTOMATON_LOGROTATE_DIR': str(rotate)}
            run = lambda: subprocess.run(['bash', str(REPO / 'docker/install/fail2ban.sh'), '--install-dir', str(install)],
                                         env=env, capture_output=True, text=True)
            result = run()
            self.assertEqual(result.returncode, 0, result.stderr)
            jail = (etc / 'jail.d/automaton.conf').read_text()
            self.assertIn(f'logpath = {volume}/logs/access.log', jail)
            self.assertNotIn('@TRAEFIK_LOG@', jail)
            self.assertIn('DOCKER-USER', jail)
            self.assertTrue((etc / 'filter.d/automaton-scanner.conf').is_file())
            self.assertIn('copytruncate', (rotate / 'automaton-traefik').read_text())
            self.assertTrue((volume / 'logs/access.log').is_file())
            allowlist = etc / 'jail.d/automaton-allowlist.local'
            allowlist.write_text('[DEFAULT]\nignoreip = 127.0.0.1/8 ::1 203.0.113.7\n')
            self.assertEqual(run().returncode, 0)
            self.assertIn('203.0.113.7', allowlist.read_text())


if __name__ == '__main__':
    unittest.main()
