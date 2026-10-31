#!/usr/bin/env python3
"""Exercise the standalone updater with disposable manifests and mocked service commands."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
IMAGES = ('API', 'WORKER', 'STUDIO', 'MYSQL', 'RABBITMQ', 'TRAEFIK', 'RUNNER')
APPLICATION_IMAGES = ('API', 'WORKER', 'STUDIO', 'STORE')


def manifest(app_digit, infrastructure_digit='0', store=True):
    names = IMAGES + (('STORE',) if store else ())
    return ''.join(f'{name}_IMAGE=example.invalid/{name.lower()}@sha256:{(app_digit if name in APPLICATION_IMAGES else infrastructure_digit) * 64}\n'
                   for name in names)


def compose(store=True, mysql_image='mysql'):
    # JSON is valid YAML; the fake docker prints it for `compose config --format json`.
    services = {name: {'image': name} for name in ('mysql', 'rabbitmq', 'api', 'worker', 'studio', 'traefik')}
    services['mysql']['image'] = mysql_image
    if store:
        services['store'] = {'image': 'store'}
    return json.dumps({'services': services, 'volumes': {'mysql-data': {}}})


@unittest.skipUnless(hasattr(os, 'geteuid') and os.geteuid() == 0, 'Requires root on Ubuntu; does not touch a live installation.')
class ServerUpdaterTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='automaton-updater-test-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.install = self.root / 'install'
        self.config = self.root / 'config'
        self.install.mkdir()
        self.config.mkdir()
        (self.install / 'release.env').write_text(manifest('1', store=False))
        (self.install / 'compose.yaml').write_text(compose(store=False))
        (self.install / '.env').write_text(f'CONFIG_DIR={self.config}\n')
        (self.install / 'version').write_text('server-v0.1.1\n')
        (self.config / 'installation.json').write_text('{"domain":"example.invalid"}\n')
        (self.config / 'appsettings.Production.json').write_text('{"Cors":{"AllowedOrigins":["https://studio.example.invalid"]}}\n')
        (self.config / 'dynamic').mkdir()
        (self.config / 'dynamic/installation.yml').write_text('http:\n  routers: {}\n')
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        docker = self.bin / 'docker'
        docker.write_text('''#!/usr/bin/env python3
import io, json, os, sys, tarfile
args=sys.argv[1:]
if os.environ.get('FAKE_DOCKER_LOG'):
    with open(os.environ['FAKE_DOCKER_LOG'], 'a') as log: print(json.dumps(args), file=log)
if args[0]=='inspect': print('true')
elif args[0]=='cp':
    with tarfile.open(fileobj=sys.stdout.buffer, mode='w|') as archive:
        data=b'key-or-data'
        item=tarfile.TarInfo('entry'); item.size=len(data)
        archive.addfile(item, io.BytesIO(data))
elif args[0]=='compose':
    if 'config' in args and '--format' in args: print(open(args[args.index('-f')+1]).read())
    elif 'config' in args and '--services' in args: print(*json.load(open(args[args.index('-f')+1]))['services'], sep=chr(10))
    elif 'ps' in args: print('container-'+args[-1])
    elif 'exec' in args: print('-- Dump completed on 2026-01-01')
    elif 'up' in args and os.environ.get('FAKE_UP_FAIL')=='1': sys.exit(1)
''')
        (self.bin / 'curl').write_text('#!/bin/sh\necho \'{"installationComplete":true}\'\n')
        (self.bin / 'df').write_text('#!/bin/sh\nprintf "Filesystem 1024-blocks Used Available Capacity Mounted\\nfixture 30000000 1000000 29000000 4%% /\\n"\n')
        for tool in self.bin.iterdir():
            tool.chmod(0o755)
        self.environment = os.environ | {'PATH': str(self.bin) + ':' + os.environ['PATH']}

    def bundle(self, target=None, compose_file=None):
        source = self.root / 'bundle-source' / 'automaton-server'
        source.mkdir(parents=True, exist_ok=True)
        (source / 'release.env').write_text(target or manifest('2'))
        (source / 'compose.yaml').write_text(compose_file or compose())
        shutil.copy2(REPO / 'docker/install/configure.py', source / 'configure.py')
        (source / 'version').write_text('server-v0.1.2\n')
        shutil.copy2(REPO / 'scripts/update-automaton.sh', source / 'update.sh')
        shutil.copy2(REPO / 'scripts/install-automaton.sh', source / 'install.sh')
        archive = self.root / 'automaton-server.tar.gz'
        with tarfile.open(archive, 'w:gz') as tar:
            tar.add(source, arcname='automaton-server')
        checksum = self.root / 'automaton-server.tar.gz.sha256'
        checksum.write_text(f'{hashlib.sha256(archive.read_bytes()).hexdigest()}  automaton-server.tar.gz\n')
        return archive, checksum

    def update(self, archive, checksum, extra=None):
        return subprocess.run(['bash', str(REPO / 'scripts/update-automaton.sh'), '--version', 'server-v0.1.2',
                               '--install-dir', str(self.install), '--config-dir', str(self.config),
                               '--bundle', str(archive), '--checksum', str(checksum)],
                              env=self.environment | (extra or {}), capture_output=True, text=True)

    def test_upgrades_after_verified_backup(self):
        result = self.update(*self.bundle())
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.install / 'version').read_text().strip(), 'server-v0.1.2')
        self.assertEqual((self.install / 'release.env').read_text(), manifest('2'))
        backup, = (self.install / 'backups').iterdir()
        for name in ('database.sql', 'config.tar.gz', 'api-keys.tar.gz', 'worker-keys.tar.gz', 'api-data.tar.gz', 'release.env', 'SHA256SUMS'):
            self.assertTrue((backup / name).is_file(), name)
        self.assertEqual((backup / 'release.env').read_text(), manifest('1', store=False))
        self.assertEqual(backup.stat().st_mode & 0o777, 0o700)

    def test_adds_app_store_to_installation_without_it(self):
        log = self.root / 'docker.log'
        result = self.update(*self.bundle(), extra={'FAKE_DOCKER_LOG': str(log)})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.install / 'release.env').read_text(), manifest('2'))
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        up, = [args for args in calls if args[0] == 'compose' and 'up' in args]
        pull, = [args for args in calls if args[0] == 'compose' and 'pull' in args]
        self.assertEqual(sorted(up[up.index('600') + 1:]), ['api', 'store', 'studio', 'worker'])
        self.assertEqual(sorted(pull[pull.index('pull') + 1:]), ['api', 'store', 'studio', 'worker'])
        self.assertNotIn('mysql', up)
        settings = json.loads((self.config / 'appsettings.Production.json').read_text())
        self.assertIn('https://store.example.invalid', settings['Cors']['AllowedOrigins'])
        edge = json.loads((self.config / 'dynamic/store.yml').read_text())
        self.assertEqual(edge['http']['routers']['store']['rule'], 'Host(`store.example.invalid`)')
        self.assertEqual((self.config / 'dynamic/installation.yml').read_text(), 'http:\n  routers: {}\n')
        self.assertIn('https://api.example.invalid', (self.config / 'store-runtime-config.js').read_text())
        # The recovery backup holds the configuration from before the store was added.
        backup, = (self.install / 'backups').iterdir()
        with tarfile.open(backup / 'config.tar.gz') as archive:
            names = archive.getnames()
        self.assertNotIn(f'{self.config.name}/store-runtime-config.js', names)

    def test_missing_store_dns_warns_without_failing(self):
        (self.bin / 'curl').write_text('#!/bin/sh\ncase "$*" in\n  *store.example.invalid*) exit 6 ;;\n  *) echo \'{"installationComplete":true}\' ;;\nesac\n')
        result = self.update(*self.bundle())
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Point a DNS record for store.example.invalid', result.stderr)
        self.assertEqual((self.install / 'version').read_text().strip(), 'server-v0.1.2')

    def test_override_sidecar_is_not_pulled_or_required(self):
        override = self.install / 'compose.override.yaml'
        override.write_text(json.dumps({'services': {'backup-sidecar': {'image': 'sidecar'}}}))
        log = self.root / 'docker.log'
        result = self.update(*self.bundle(), extra={'FAKE_DOCKER_LOG': str(log)})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        for args in calls:
            if args[0] == 'compose' and ('pull' in args or 'up' in args or 'ps' in args):
                self.assertNotIn('backup-sidecar', args)

    def test_removed_service_or_volume_change_is_refused(self):
        without_worker = json.loads(compose())
        del without_worker['services']['worker']
        changed_volumes = json.loads(compose())
        changed_volumes['volumes'] = {'mysql-data': {'external': True}}
        for candidate, message in ((without_worker, 'removes a service'), (changed_volumes, 'changes Compose volumes')):
            result = self.update(*self.bundle(compose_file=json.dumps(candidate)))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(message, result.stderr)
            self.assertFalse((self.install / 'backups').exists())

    def test_invalid_operator_settings_stop_before_services_change(self):
        (self.config / 'appsettings.Production.json').write_text('{"Cors": {"AllowedOrigins": "https://studio.example.invalid"}}')
        log = self.root / 'docker.log'
        result = self.update(*self.bundle(), extra={'FAKE_DOCKER_LOG': str(log)})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Cors.AllowedOrigins as a list', result.stderr)
        self.assertIn('config.tar.gz', result.stderr)
        self.assertFalse(any('up' in json.loads(line) for line in log.read_text().splitlines()))
        self.assertEqual((self.install / 'release.env').read_text(), manifest('1', store=False))

    def test_compose_infrastructure_change_preserves_installation(self):
        result = self.update(*self.bundle(compose_file=compose(mysql_image='mysql-next')))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('changes mysql', result.stderr)
        self.assertEqual((self.install / 'release.env').read_text(), manifest('1', store=False))
        self.assertFalse((self.install / 'backups').exists())

    def old_backups(self, count):
        backups = self.install / 'backups'
        backups.mkdir(mode=0o700, exist_ok=True)
        created = []
        for index in range(count):
            folder = backups / f'server-v0.0.{index}-2026010{index}T000000Z-abc12{index}'
            folder.mkdir()
            os.utime(folder, (1_700_000_000 + index, 1_700_000_000 + index))
            created.append(folder)
        manual = backups / 'broker-listener-20260101T000000Z'
        manual.mkdir()
        os.utime(manual, (1_600_000_000, 1_600_000_000))
        return created, manual

    def test_successful_update_rotates_backups_and_previous_images(self):
        created, manual = self.old_backups(4)
        log = self.root / 'docker.log'
        result = self.update(*self.bundle(), extra={'FAKE_DOCKER_LOG': str(log)})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        remaining = sorted(p.name for p in (self.install / 'backups').iterdir())
        new_backup = [p for p in (self.install / 'backups').iterdir() if p.name.startswith('server-v0.1.2-')]
        self.assertEqual(len(new_backup), 1)
        # Newest three update backups: the new one plus the two most recent old ones.
        self.assertEqual(sorted(remaining), sorted([new_backup[0].name, created[3].name, created[2].name, manual.name]))
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        removed = [args[-1] for args in calls if args[:2] == ['image', 'rm']]
        self.assertEqual(sorted(removed), sorted(f'example.invalid/{name}@sha256:{"1" * 64}' for name in ('api', 'worker', 'studio')))
        self.assertIn('Free disk space:', result.stdout)

    def test_keep_backups_option_and_failed_update_keep_everything(self):
        created, manual = self.old_backups(3)
        failed = self.update(*self.bundle(), extra={'FAKE_UP_FAIL': '1'})
        self.assertNotEqual(failed.returncode, 0)
        for folder in created + [manual]:
            self.assertTrue(folder.exists(), folder)
        archive, checksum = self.bundle()
        result = subprocess.run(['bash', str(REPO / 'scripts/update-automaton.sh'), '--version', 'server-v0.1.2',
                                 '--install-dir', str(self.install), '--config-dir', str(self.config),
                                 '--bundle', str(archive), '--checksum', str(checksum), '--keep-backups', '1'],
                                env=self.environment, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        update_backups = [p for p in (self.install / 'backups').iterdir() if p.name.startswith('server-v')]
        self.assertEqual(len(update_backups), 1)
        self.assertTrue(manual.exists())
        invalid = subprocess.run(['bash', str(REPO / 'scripts/update-automaton.sh'), '--keep-backups', '0'],
                                 env=self.environment, capture_output=True, text=True)
        self.assertIn('--keep-backups must be 1 or more', invalid.stderr)

    def test_bad_checksum_and_infrastructure_change_preserve_installation(self):
        archive, checksum = self.bundle()
        checksum.write_text('0' * 64 + '  automaton-server.tar.gz\n')
        self.assertNotEqual(self.update(archive, checksum).returncode, 0)
        archive, checksum = self.bundle(manifest('2', '3'))
        result = self.update(archive, checksum)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('reviewed infrastructure migration', result.stderr)
        self.assertEqual((self.install / 'release.env').read_text(), manifest('1', store=False))
        self.assertFalse((self.install / 'backups').exists())

    def test_failed_service_update_keeps_previous_manifest_and_backup(self):
        result = self.update(*self.bundle(), extra={'FAKE_UP_FAIL': '1'})
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.install / 'release.env').read_text(), manifest('1', store=False))
        backup, = (self.install / 'backups').iterdir()
        self.assertTrue((backup / 'database.sql').is_file())

    def test_preserves_and_uses_reviewed_override_for_both_manifests(self):
        override = self.install / 'compose.override.yaml'
        content = 'services:\n  mysql:\n    image: example.invalid/retained-mysql@sha256:' + '9' * 64 + '\n'
        override.write_text(content)
        log = self.root / 'docker.log'
        result = self.update(*self.bundle(), extra={'FAKE_DOCKER_LOG': str(log)})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        # `config --services` deliberately lists release-owned services without the override.
        compose = [args for args in calls if args[0] == 'compose' and '-f' in args and '--services' not in args]
        self.assertTrue(compose)
        for args in compose:
            self.assertIn(str(override), args)
        backup, = (self.install / 'backups').iterdir()
        self.assertEqual((backup / 'compose.override.yaml').read_text(), content)
        self.assertIn('compose.override.yaml', (backup / 'SHA256SUMS').read_text())
        self.assertEqual(override.read_text(), content)

    def test_rejects_symlink_override_before_services_change(self):
        override = self.install / 'compose.override.yaml'
        override.symlink_to(self.install / 'compose.yaml')
        result = self.update(*self.bundle())
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('non-symlink', result.stderr)
        self.assertFalse((self.install / 'backups').exists())

    def test_no_version_selects_latest_stable_distribution(self):
        (self.bin / 'curl').write_text('''#!/bin/sh
case "$*" in
  *'/token?'*) echo '{"token":"test-token"}' ;;
  *'/tags/list'*) echo '{"tags":["server-v0.1.2","server-v0.2.0-beta.1","server-v0.1.10","other-v9.0.0"]}' ;;
  *) echo '{"installationComplete":true}' ;;
esac
''')
        (self.bin / 'docker').write_text('#!/bin/sh\nif [ "$1" = pull ]; then echo "selected:$2" >&2; exit 1; fi\n')
        command = ['bash', str(REPO / 'scripts/update-automaton.sh'),
                   '--install-dir', str(self.install), '--config-dir', str(self.config)]
        result = subprocess.run(command, env=self.environment, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('selected:ghcr.io/rzvdaniel/automaton-server-distribution:server-v0.1.10', result.stderr)
        self.assertFalse((self.install / 'backups').exists())


if __name__ == '__main__':
    unittest.main()
