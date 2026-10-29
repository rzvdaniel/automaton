#!/usr/bin/env python3
"""Validate the installer entrypoint against disposable paths without starting services."""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[2]


@unittest.skipUnless(os.geteuid() == 0, 'Requires root and existing Docker/Compose on Ubuntu 24.04; never installs Docker.')
class InstallerEntrypointTests(unittest.TestCase):
    def setUp(self):
        tools = tempfile.TemporaryDirectory(prefix='automaton-installer-tools-')
        self.addCleanup(tools.cleanup)
        real_docker = shutil.which('docker')
        docker = Path(tools.name) / 'docker'
        docker.write_text(f'''#!/usr/bin/env python3
import os, sys
args = sys.argv[1:]
if args[:2] in (['ps', '-aq'], ['volume', 'ls']):
    sys.exit(0)
if args == ['info'] or args == ['compose', 'version'] or (args[:1] == ['compose'] and args[-2:] == ['config', '--quiet']):
    os.execv({real_docker!r}, [{real_docker!r}] + args)
raise SystemExit('Unexpected Docker operation in installer fixture')
''')
        docker.chmod(0o755)
        environment = patch.dict(os.environ, PATH=tools.name + ':' + os.environ['PATH'])
        environment.start()
        self.addCleanup(environment.stop)

    def test_no_version_selects_latest_stable_server_release(self):
        with tempfile.TemporaryDirectory(prefix='automaton-installer-test-') as temporary:
            root = Path(temporary)
            tools = root / 'bin'; tools.mkdir()
            (root / 'sitecustomize.py').write_text('''import io, json, urllib.request, urllib.parse
def release_page(request, timeout):
    page = json.dumps([
        {'tag_name': 'server-v0.1.2', 'draft': False, 'prerelease': False},
        {'tag_name': 'server-v0.1.10', 'draft': False, 'prerelease': False},
        {'tag_name': 'server-v0.2.0', 'draft': True, 'prerelease': False},
        {'tag_name': 'other-v9.0.0', 'draft': False, 'prerelease': False},
    ] if urllib.parse.parse_qs(urllib.parse.urlsplit(request.full_url).query).get('page') == ['1'] else [])
    return io.BytesIO(page.encode())
urllib.request.urlopen = release_page
''')
            (tools / 'curl').write_text('#!/bin/sh\nprintf "selected:%s\\n" "$*" >&2\nexit 1\n')
            (tools / 'curl').chmod(0o755)
            result = subprocess.run(['bash', str(REPO / 'scripts/install-automaton.sh'),
                                     '--install-dir', str(root / 'install'), '--config-dir', str(root / 'config')],
                                    env=os.environ | {'PATH': str(tools) + ':' + os.environ['PATH'],
                                                      'PYTHONPATH': str(root)},
                                    capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Installing latest stable Automaton server release: server-v0.1.10', result.stdout, result.stderr)
            self.assertIn('/server-v0.1.10/automaton-server.tar.gz', result.stderr)

    def test_fresh_install_checks_existing_filesystem_before_port_preflight(self):
        with tempfile.TemporaryDirectory(prefix='automaton-installer-test-') as temporary:
            root = Path(temporary)
            bundle = root / 'bundle'; bundle.mkdir()
            (bundle / 'release.env').write_text('')
            shutil.copy2(REPO / 'docker/install/configure.py', bundle / 'configure.py')
            tools = root / 'bin'; tools.mkdir()
            # Match df's failure for missing paths, with enough disk on existing ancestors.
            (tools / 'df').write_text('#!/bin/bash\n[[ -d "${@: -1}" ]] || exit 1\nprintf "Filesystem 1024-blocks Used Available Capacity Mounted\\nfixture 30000000 1000000 29000000 4%% /\\n"\n')
            (tools / 'ss').write_text('#!/bin/sh\necho "LISTEN 0 128 0.0.0.0:80 0.0.0.0:*"\n')
            for tool in tools.iterdir(): tool.chmod(0o755)
            command = ['bash', str(REPO / 'scripts/install-automaton.sh'),
                       '--install-dir', str(root / 'missing/nested/install'), '--config-dir', str(root / 'config'),
                       '--email', 'owner@example.invalid', '--domain', 'example.invalid']
            environment = os.environ | {'AUTOMATON_RELEASE_DIR': str(bundle), 'PATH': str(tools) + ':' + os.environ['PATH']}
            result = subprocess.run(command, env=environment, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Ports 80, 443 and 5671 must be available.', result.stderr)
            self.assertFalse((root / 'missing').exists())

    def test_resume_and_configuration_identity(self):
        with tempfile.TemporaryDirectory(prefix='automaton-installer-test-') as temporary:
            root = Path(temporary)
            bundle = root / 'bundle'; bundle.mkdir()
            for name in ('configure.py', 'compose.yaml'):
                shutil.copy2(REPO / 'docker/install' / name, bundle / name)
            images = ('API', 'STUDIO', 'WORKER', 'MYSQL', 'RABBITMQ', 'TRAEFIK', 'RUNNER')
            (bundle / 'release.env').write_text(''.join(f'{name}_IMAGE=example.invalid/test@sha256:{"0" * 64}\n' for name in images))
            command = ['bash', str(REPO / 'scripts/install-automaton.sh'), '--configure-only',
                       '--install-dir', str(root / 'install'), '--config-dir', str(root / 'config'),
                       '--email', 'owner@example.invalid', '--domain', 'example.invalid']
            environment = os.environ | {'AUTOMATON_RELEASE_DIR': str(bundle)}
            first = subprocess.run(command, env=environment, capture_output=True, text=True)
            self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
            def snapshot():
                return {p.relative_to(root): hashlib.sha256(p.read_bytes()).hexdigest()
                        for p in root.rglob('*') if p.is_file()}
            before = snapshot()
            second = subprocess.run(command, env=environment, capture_output=True, text=True)
            self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
            self.assertEqual(before, snapshot())
            changed = subprocess.run(command + ['--domain', 'different.invalid'], env=environment, capture_output=True, text=True)
            self.assertNotEqual(changed.returncode, 0)
            self.assertEqual(before, snapshot())
            self.assertIn('CONFIG_DIR=' + str(root / 'config'), (root / 'install/.env').read_text())
            # A retained release manifest proves this is recovery, even if the
            # entire private configuration directory was accidentally deleted.
            shutil.rmtree(root / 'config')
            recovered = subprocess.run(command, env=environment, capture_output=True, text=True)
            self.assertNotEqual(recovered.returncode, 0)
            self.assertIn('Restore the configuration backup', recovered.stderr)
            self.assertFalse((root / 'config').exists())


if __name__ == '__main__': unittest.main()
