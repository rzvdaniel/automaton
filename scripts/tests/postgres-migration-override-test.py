#!/usr/bin/env python3
"""Check migration override selection and rejection before any service is stopped."""
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
SCRIPT = REPO / 'docker/install/migrate-to-postgresql.sh'


class OverridePreflightTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='automaton-migration-override-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.install = self.root / 'install'
        self.config = self.root / 'config'
        self.bundle = self.root / 'bundle'
        self.bin = self.root / 'bin'
        for directory in (self.install, self.config / 'secrets', self.bundle, self.bin):
            directory.mkdir(parents=True)
        (self.install / '.env').write_text(f'CONFIG_DIR={self.config}\n')
        (self.install / 'release.env').write_text('MYSQL_IMAGE=mysql:8\nRUNNER_IMAGE=runner:test\n')
        (self.bundle / 'release.env').write_text('POSTGRES_IMAGE=postgres:16\nRUNNER_IMAGE=runner:test\n')
        (self.config / 'secrets/mysql-password').write_text('test-only\n')
        (self.config / 'installation.json').write_text('{"domain":"example.invalid"}')
        (self.config / 'appsettings.Production.json').write_text('{}')
        (self.bundle / 'version').write_text('server-v0.0.0')
        old = {'name': 'scratch-migration', 'services': {
            'mysql': {'volumes': [{'type': 'volume', 'source': 'mysql-data', 'target': '/var/lib/mysql'}]},
            'rabbitmq': {}, 'api': {}, 'worker': {}},
            'volumes': {'mysql-data': {'name': 'scratch_external_mysql', 'external': True}}}
        new = {'name': 'scratch-migration', 'services': {
            'postgres': {'volumes': [{'type': 'volume', 'source': 'postgres-data', 'target': '/var/lib/postgresql/data'}]},
            'rabbitmq': {}, 'api': {}, 'worker': {}},
            'volumes': {'postgres-data': {'name': 'scratch-migration_postgres-data'}}}
        (self.install / 'compose.yaml').write_text(json.dumps(old))
        (self.bundle / 'compose.yaml').write_text(json.dumps(new))
        self.old_override = self.install / 'compose.override.yaml'
        self.old_override.write_text(json.dumps({'services': {'mysql': {'image': 'mysql:8'}, 'api': {'ports': ['127.0.0.1::8080']}},
                                                 'networks': {'private': {'external': True}}}))
        self.postgres_override = self.root / 'postgres-override.yaml'
        self.postgres_override.write_text(json.dumps({'services': {'api': {'ports': ['127.0.0.1::8080']}},
                                                       'networks': {'private': {'external': True}},
                                                       'volumes': {'api-data': {'external': True}}}))
        for name in ('update.sh', 'install.sh', 'configure.py', 'migrate-to-postgresql.sh',
                     'mysql_to_postgres.py', 'migration_app_check.py'):
            (self.bundle / name).write_text('')
        self.log = self.root / 'docker.log'
        docker = self.bin / 'docker'
        docker.write_text('''#!/usr/bin/env python3
import io, json, os, sys, tarfile
args = sys.argv[1:]
with open(os.environ['FAKE_DOCKER_LOG'], 'a') as log: print(json.dumps(args), file=log)
if args[0] == 'compose':
    if 'config' in args:
        paths = [args[i+1] for i, arg in enumerate(args) if arg == '-f']
        data = json.load(open(paths[0]))
        for path in paths[1:]:
            extra = json.load(open(path))
            for key, value in extra.items():
                if key == 'services':
                    for name, fields in value.items(): data.setdefault('services', {}).setdefault(name, {}).update(fields)
                elif isinstance(value, dict): data.setdefault(key, {}).update(value)
                else: data[key] = value
        if '--services' in args: print(*data['services'], sep='\\n')
        elif '--format' in args: print(json.dumps(data))
    elif 'ps' in args: print('container-' + args[-1])
    elif 'exec' in args and 'mysql' in args:
        if 'mysqldump' in ' '.join(args): print('CREATE TABLE example (id int);')
        else: print('1')
elif args[0] == 'cp':
    with tarfile.open(fileobj=sys.stdout.buffer, mode='w|') as archive:
        content = b'test-only'
        member = tarfile.TarInfo('data'); member.size = len(content)
        archive.addfile(member, io.BytesIO(content))
elif args[0] == 'ps':
    if '--format' in args: print('mysql')
    elif any('service=postgres' in arg for arg in args): print('postgres-container')
    else: print('mysql-container')
elif args[:2] == ['volume', 'ls']:
    if any('scratch_external_mysql' in arg for arg in args): print('scratch_external_mysql')
elif args[0] == 'inspect':
    template = ' '.join(args)
    if '.State.Health.Status' in template: print('healthy')
    elif '.State.Running' in template: print('true')
    else: print('scratch-network')
''')
        docker.chmod(0o755)
        self.env = os.environ | {'PATH': str(self.bin) + ':' + os.environ['PATH'],
                                 'FAKE_DOCKER_LOG': str(self.log), 'AUTOMATON_LOCK_FILE': str(self.root / 'lock')}

    def run_plan(self, override=None):
        return subprocess.run(['bash', str(SCRIPT), '--plan', '--rehearsal', '--skip-app-check',
                               '--bundle-dir', str(self.bundle), '--install-dir', str(self.install),
                               '--config-dir', str(self.config), '--postgres-override', str(override or self.postgres_override)],
                              env=self.env, capture_output=True, text=True)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def test_old_and_new_stack_use_different_overrides(self):
        result = self.run_plan()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        config_calls = [args for args in self.calls() if args[0] == 'compose' and 'config' in args]
        self.assertTrue(any(str(self.old_override) in args and str(self.postgres_override) not in args for args in config_calls))
        self.assertTrue(any(any(path.endswith('/postgres-override.yaml') for path in args)
                            and str(self.old_override) not in args for args in config_calls))
        self.assertIn('Source data:', result.stdout)

    @unittest.skipUnless(shutil.which('docker'), 'Docker Compose is unavailable')
    def test_production_like_overrides_pass_real_compose_config(self):
        for path in (self.install / 'compose.yaml', self.bundle / 'compose.yaml'):
            config = json.loads(path.read_text())
            for service in config['services'].values():
                service['image'] = 'busybox:latest'
            path.write_text(json.dumps(config))
        for path in (self.old_override, self.postgres_override):
            config = json.loads(path.read_text())
            config['services']['api']['networks'] = ['private']
            config['services']['api']['volumes'] = [{'type': 'volume', 'source': 'api-data', 'target': '/app/AppData'}]
            config.setdefault('volumes', {})['api-data'] = {'external': True, 'name': 'scratch_external_api_data'}
            path.write_text(json.dumps(config))
        for manifest, override in ((self.install / 'compose.yaml', self.old_override),
                                   (self.bundle / 'compose.yaml', self.postgres_override)):
            result = subprocess.run(['docker', 'compose', '-p', 'scratch-migration', '-f', str(manifest),
                                     '-f', str(override), 'config', '--format', 'json'],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            config = json.loads(result.stdout)
            self.assertTrue(config['networks']['private']['external'])
            self.assertTrue(config['volumes']['api-data']['external'])
            self.assertEqual(len(config['services']['api']['ports']), 1)
            if manifest.parent == self.install:
                self.assertEqual(config['volumes']['mysql-data']['name'], 'scratch_external_mysql')
            else:
                self.assertNotIn('mysql', config['services'])

    def test_mysql_service_in_postgres_override_is_refused_before_loader(self):
        self.postgres_override.write_text(json.dumps({'services': {'mysql': {'image': 'mysql:8'}}}))
        result = self.run_plan()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('adds a mysql service', result.stderr)
        self.assertFalse(any(args[0] == 'run' for args in self.calls()))

    def test_mysql_connection_in_postgres_override_is_refused_before_loader(self):
        self.postgres_override.write_text(json.dumps({'services': {'api': {'environment': {'ConnectionStrings__MySqlConnection': 'Host=mysql'}}}}))
        result = self.run_plan()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('MySQL connection string', result.stderr)
        self.assertFalse(any(args[0] == 'run' for args in self.calls()))

    def test_legacy_allow_override_cannot_carry_mysql_into_postgres(self):
        result = subprocess.run(['bash', str(SCRIPT), '--plan', '--rehearsal', '--skip-app-check',
                                 '--allow-override', '--bundle-dir', str(self.bundle),
                                 '--install-dir', str(self.install), '--config-dir', str(self.config)],
                                env=self.env, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('adds a mysql service', result.stderr)

    def test_run_uses_external_mysql_volume_and_installs_postgres_override(self):
        result = subprocess.run(['bash', str(SCRIPT), '--run', '--rehearsal', '--skip-app-check',
                                 '--confirm', 'example.invalid', '--bundle-dir', str(self.bundle),
                                 '--install-dir', str(self.install), '--config-dir', str(self.config),
                                 '--postgres-override', str(self.postgres_override)],
                                env=self.env, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.old_override.read_bytes(), self.postgres_override.read_bytes())
        self.assertIn('Migrated to PostgreSQL', result.stdout)
        recovery, = (self.install / 'backups').iterdir()
        self.assertIn('mysql_volume_name=scratch_external_mysql', (recovery / 'NOTES').read_text())
        self.assertEqual((recovery / 'STATE').read_text().strip(), 'complete')
        self.assertTrue((recovery / 'mysql.sql.gz').is_file())

    def test_rollback_removes_postgres_override_when_installation_had_none(self):
        self.old_override.unlink()
        result = subprocess.run(['bash', str(SCRIPT), '--run', '--rehearsal', '--skip-app-check',
                                 '--confirm', 'example.invalid', '--bundle-dir', str(self.bundle),
                                 '--install-dir', str(self.install), '--config-dir', str(self.config),
                                 '--postgres-override', str(self.postgres_override)],
                                env=self.env, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(self.old_override.is_file())
        rollback = self.rollback()
        self.assertEqual(rollback.returncode, 0, rollback.stdout + rollback.stderr)
        self.assertFalse(self.old_override.exists())

    def prepare_switched_recovery(self):
        recovery = self.root / 'recovery'
        recovery.mkdir()
        for name in ('release.env', 'compose.yaml', '.env', 'compose.override.yaml'):
            (recovery / name).write_bytes((self.install / name).read_bytes())
        with tarfile.open(recovery / 'config.tar.gz', 'w:gz') as archive:
            archive.add(self.config, arcname=self.config.name)
        (recovery / 'config-added.txt').write_text('')
        (recovery / 'NOTES').write_text('postgres_volume_created=1\npostgres_volume_name=scratch-migration_postgres-data\n'
                                        'mysql_volume_name=scratch_external_mysql\nbundle_version=server-v0.0.0\n')
        (recovery / 'STATE').write_text('switched\n')
        (recovery / 'before.json').write_text('{}')
        (recovery / 'postgres-override.yaml').write_bytes(self.postgres_override.read_bytes())
        with (recovery / 'SHA256SUMS').open('w') as sums:
            for name in ('release.env', 'compose.yaml', '.env', 'compose.override.yaml', 'config.tar.gz', 'postgres-override.yaml'):
                sums.write(f'{hashlib.sha256((recovery / name).read_bytes()).hexdigest()}  {name}\n')
        (self.install / '.postgresql-migration').write_text(str(recovery))
        return recovery

    def rollback(self):
        return subprocess.run(['bash', str(SCRIPT), '--rollback', '--rehearsal', '--confirm', 'example.invalid',
                               '--install-dir', str(self.install), '--config-dir', str(self.config)],
                              env=self.env, capture_output=True, text=True)

    def test_switched_rollback_restores_mysql_override(self):
        recovery = self.prepare_switched_recovery()
        original_override = self.old_override.read_text()
        rollback = self.rollback()
        self.assertEqual(rollback.returncode, 0, rollback.stdout + rollback.stderr)
        self.assertEqual(self.old_override.read_text(), original_override)
        self.assertEqual((recovery / 'STATE').read_text().strip(), 'rolled-back')

    def test_finish_refuses_missing_saved_postgres_override(self):
        recovery = self.prepare_switched_recovery()
        with (recovery / 'NOTES').open('a') as notes:
            notes.write('postgres_override_saved=1\n')
        (recovery / 'postgres-override.yaml').unlink()
        finish = subprocess.run(['bash', str(SCRIPT), '--finish', '--rehearsal', '--skip-app-check',
                                 '--bundle-dir', str(self.bundle), '--install-dir', str(self.install),
                                 '--config-dir', str(self.config)], env=self.env, capture_output=True, text=True)
        self.assertNotEqual(finish.returncode, 0)
        self.assertIn('saved PostgreSQL override is missing', finish.stderr)
        self.assertEqual((recovery / 'STATE').read_text().strip(), 'switched')

    def test_finish_installs_postgres_override_and_completed_rollback_restores_mysql_override(self):
        recovery = self.prepare_switched_recovery()
        original_override = self.old_override.read_text()
        finish = subprocess.run(['bash', str(SCRIPT), '--finish', '--rehearsal', '--skip-app-check',
                                 '--bundle-dir', str(self.bundle), '--install-dir', str(self.install),
                                 '--config-dir', str(self.config)], env=self.env, capture_output=True, text=True)
        self.assertEqual(finish.returncode, 0, finish.stdout + finish.stderr)
        self.assertEqual(self.old_override.read_bytes(), self.postgres_override.read_bytes())
        self.assertEqual((recovery / 'STATE').read_text().strip(), 'complete')
        rollback = self.rollback()
        self.assertEqual(rollback.returncode, 0, rollback.stdout + rollback.stderr)
        self.assertEqual(self.old_override.read_text(), original_override)
        self.assertEqual((recovery / 'STATE').read_text().strip(), 'rolled-back')

    def test_symlink_postgres_override_is_refused(self):
        self.postgres_override.unlink()
        self.postgres_override.symlink_to(self.old_override)
        result = self.run_plan()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('regular, non-symlink', result.stderr)


if __name__ == '__main__':
    unittest.main()
