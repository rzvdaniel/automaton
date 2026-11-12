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
IMAGES = ('API', 'WORKER', 'STUDIO', 'POSTGRES', 'RABBITMQ', 'TRAEFIK', 'RUNNER', 'AGE')
APPLICATION_IMAGES = ('API', 'WORKER', 'STUDIO', 'STORE', 'AGE')


def manifest(app_digit, infrastructure_digit='0', store=True, age=True):
    names = IMAGES + (('STORE',) if store else ())
    if not age:
        names = tuple(name for name in names if name != 'AGE')
    return ''.join(f'{name}_IMAGE=example.invalid/{name.lower()}@sha256:{(app_digit if name in APPLICATION_IMAGES else infrastructure_digit) * 64}\n'
                   for name in names)


def compose(store=True, postgres_image='postgres', update_mounts=False):
    # JSON is valid YAML; the fake docker prints it for `compose config --format json`.
    services = {name: {'image': name} for name in ('postgres', 'rabbitmq', 'api', 'worker', 'studio', 'traefik')}
    services['postgres']['image'] = postgres_image
    if update_mounts:
        services['api']['volumes'] = [
            {'type': 'bind', 'source': '/var/lib/automaton/update/requests', 'target': '/app/update-requests'},
            {'type': 'bind', 'source': '/var/lib/automaton/update/status', 'target': '/app/update-status', 'read_only': True}]
    if store:
        services['store'] = {'image': 'store'}
    return json.dumps({'services': services, 'volumes': {'postgres-data': {}}})


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
        (self.install / 'release.env').write_text(manifest('1', store=False, age=False))
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
elif args[:2]==['image','ls']:
    for line in os.environ.get('FAKE_IMAGE_LS','').split(';'):
        if line: print(line)
elif args[:2]==['image','inspect'] and '{{.Created}} {{.Id}}' in args:
    created=dict(item.split('=',1) for item in os.environ.get('FAKE_IMAGE_CREATED','').split(';') if item)
    print(created.get(args[-1],'2000-01-01T00:00:00Z'), args[-1])
elif args[:2]==['image','inspect'] and '{{.Id}}' in args:
    print(args[-1] if args[-1].startswith('sha256:') else 'sha256:pinned')
elif args[:2]==['image','prune'] or args[:2]==['builder','prune']:
    if os.environ.get('FAKE_PRUNE_FREES')=='1': open(os.environ['FAKE_PRUNED'],'w').close()
elif args[:2]==['system','df']: print('TYPE TOTAL ACTIVE SIZE RECLAIMABLE')
elif args[0]=='cp':
    with tarfile.open(fileobj=sys.stdout.buffer, mode='w|') as archive:
        data=b'key-or-data'
        item=tarfile.TarInfo('entry'); item.size=len(data)
        archive.addfile(item, io.BytesIO(data))
elif args[0]=='compose':
    if 'config' in args and '--format' in args: print(open(args[args.index('-f')+1]).read())
    elif 'config' in args and '--services' in args: print(*json.load(open(args[args.index('-f')+1]))['services'], sep=chr(10))
    elif 'ps' in args: print('container-'+args[-1])
    elif 'exec' in args:
        # The updater runs pg_dump (custom format) and pg_restore --list in the postgres service.
        if args[args.index('exec')+1:][:2]!=['-T','postgres']: sys.exit(2)
        command=args[args.index('postgres')+1:]
        if command[0]=='pg_dump':
            if '--format=custom' not in command or command[command.index('-d')+1]!='automaton': sys.exit(2)
            sys.stdout.buffer.write(b'PGDMP-fake-custom-archive')
        elif command[:2]==['pg_restore','--list']:
            if sys.stdin.buffer.read()!=b'PGDMP-fake-custom-archive': sys.exit(1)
            print('; Archive created by fake pg_dump')
        else: sys.exit(2)
    elif 'up' in args and os.environ.get('FAKE_UP_FAIL')=='1': sys.exit(1)
''')
        (self.bin / 'curl').write_text("""#!/bin/sh
case "$*" in
  *api.example.invalid/health) echo "${FAKE_API_HEALTH-ok}" ;;
  *store.example.invalid*) [ "${FAKE_STORE_DNS_MISSING:-}" != 1 ] || exit 6 ;;
  *) echo '{"installationComplete":true}' ;;
esac
""")
        for tool in ('systemctl', 'journalctl'):
            (self.bin / tool).write_text('#!/bin/sh\n[ -z "$FAKE_DOCKER_LOG" ] || echo "[\\"%s\\", \\"$*\\"]" >> "$FAKE_DOCKER_LOG"\n' % tool)
        (self.bin / 'df').write_text('''#!/bin/sh
# FAKE_DF_LOW_KB: free space until the fake docker has pruned images (it creates $FAKE_PRUNED), then 29000000.
free=29000000
if [ -n "$FAKE_DF_LOW_KB" ] && [ ! -e "$FAKE_PRUNED" ]; then free=$FAKE_DF_LOW_KB; fi
printf "Filesystem 1024-blocks Used Available Capacity Mounted\\nfixture 30000000 1000000 %s 4%% /\\n" "$free"
''')
        for tool in self.bin.iterdir():
            tool.chmod(0o755)
        self.update_dir = self.root / 'update'
        self.systemd_dir = self.root / 'systemd'
        self.systemd_dir.mkdir()
        # Never touch the host's systemd units or /var/lib/automaton.
        self.environment = os.environ | {'PATH': str(self.bin) + ':' + os.environ['PATH'],
                                         'AUTOMATON_UPDATE_TEST': '1', 'AUTOMATON_UPDATE_DIR': str(self.update_dir),
                                         'AUTOMATON_SYSTEMD_DIR': str(self.systemd_dir)}

    def bundle(self, target=None, compose_file=None, with_update_trigger=True):
        source = self.root / 'bundle-source' / 'automaton-server'
        source.mkdir(parents=True, exist_ok=True)
        (source / 'release.env').write_text(target or manifest('2'))
        (source / 'compose.yaml').write_text(compose_file or compose())
        shutil.copy2(REPO / 'docker/install/configure.py', source / 'configure.py')
        (source / 'version').write_text('server-v0.1.2\n')
        shutil.copy2(REPO / 'scripts/update-automaton.sh', source / 'update.sh')
        shutil.copy2(REPO / 'scripts/install-automaton.sh', source / 'install.sh')
        shutil.copy2(REPO / 'docker/install/cleanup.sh', source / 'cleanup.sh')
        if with_update_trigger:
            shutil.copy2(REPO / 'docker/install/update-from-request.sh', source / 'update-from-request.sh')
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
        for name in ('database.dump', 'config.tar.gz', 'api-keys.tar.gz', 'worker-keys.tar.gz', 'api-data.tar.gz', 'release.env', 'SHA256SUMS'):
            self.assertTrue((backup / name).is_file(), name)
        self.assertEqual((backup / 'release.env').read_text(), manifest('1', store=False, age=False))
        self.assertEqual(backup.stat().st_mode & 0o777, 0o700)

    def test_backup_without_age_image_keeps_plain_backup_and_list_works(self):
        backup_script = REPO / 'docker/install/backup.sh'
        lock_file = self.root / 'backup.lock'
        environment = self.environment | {'AUTOMATON_BACKUP_LOCK_FILE': str(lock_file)}
        nightly = self.install / 'backups/nightly'
        nightly.mkdir(parents=True)
        legacy_backup = nightly / 'automaton-20260101T000000Z'
        legacy_backup.mkdir()

        listed = subprocess.run(['bash', str(backup_script), '--install-dir', str(self.install), '--list'],
                                env=environment, capture_output=True, text=True)
        self.assertEqual(listed.returncode, 0, f'{listed.stdout}\n{listed.stderr}')
        self.assertEqual(listed.stdout.strip(), legacy_backup.name)

        result = subprocess.run(['bash', str(backup_script), '--install-dir', str(self.install)],
                                env=environment, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('not encrypted', result.stderr)
        folders = [path for path in nightly.glob('automaton-*') if path.name != legacy_backup.name]
        self.assertEqual(len(folders), 1)
        self.assertTrue((folders[0] / 'SHA256SUMS').is_file())
        self.assertFalse(list(nightly.glob('*.tar.age')))

    def test_cleanup_script_removes_only_unused_images_and_old_cache_and_installs_a_timer_after_the_backup(self):
        script = REPO / 'docker/install/cleanup.sh'
        log = self.root / 'cleanup.log'
        environment = self.environment | {'FAKE_DOCKER_LOG': str(log)}
        run = lambda *args: subprocess.run(['bash', str(script), '--install-dir', str(self.install), *args],
                                           env=environment, capture_output=True, text=True)
        result = run()
        self.assertEqual(result.returncode, 0, result.stderr)
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        self.assertIn(['image', 'prune', '-a', '-f', '--filter', 'until=168h'], calls)
        self.assertIn(['builder', 'prune', '-f', '--filter', 'until=168h'], calls)
        self.assertIn(['journalctl', '--vacuum-size=200M'], calls)
        self.assertFalse(any('volume' in ' '.join(call) or '--volumes' in call for call in calls))
        result = run('--install-timer')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('After=docker.service automaton-backup.service', (self.systemd_dir / 'automaton-cleanup.service').read_text())
        self.assertIn('OnCalendar=*-*-* 05:00:00', (self.systemd_dir / 'automaton-cleanup.timer').read_text())

    def test_update_installs_the_cleanup_timer(self):
        result = self.update(*self.bundle())
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.install / 'cleanup.sh').stat().st_mode & 0o777, 0o700)
        self.assertTrue((self.systemd_dir / 'automaton-cleanup.timer').is_file())

    def test_enables_updates_from_studio_before_the_api_is_recreated(self):
        log = self.root / 'docker.log'
        # The new release mounts the update directories into the API; the compose-change rules accept it.
        result = self.update(*self.bundle(compose_file=compose(update_mounts=True)), extra={'FAKE_DOCKER_LOG': str(log)})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue((self.install / 'update-from-request.sh').is_file())
        self.assertEqual((self.install / 'update-from-request.sh').stat().st_mode & 0o777, 0o700)
        self.assertEqual((self.update_dir / 'requests').stat().st_mode & 0o777, 0o700)
        self.assertTrue((self.update_dir / 'status').is_dir())
        self.assertIn('PathExists=', (self.systemd_dir / 'automaton-update.path').read_text())
        self.assertIn('Type=oneshot', (self.systemd_dir / 'automaton-update.service').read_text())
        # Directories exist before `up` recreates the API with the bind mounts.
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        self.assertTrue(any(args[0] == 'compose' and 'up' in args for args in calls))

    def test_update_without_the_trigger_script_still_succeeds(self):
        result = self.update(*self.bundle(with_update_trigger=False))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse((self.install / 'update-from-request.sh').exists())

    def test_already_installed_release_still_enables_updates_from_studio(self):
        (self.install / 'release.env').write_text(manifest('2'))
        result = self.update(*self.bundle())
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('already installed', result.stdout)
        self.assertTrue((self.systemd_dir / 'automaton-update.path').is_file())

    def test_stops_on_mysql_installation_without_changes(self):
        legacy = manifest('1', store=False, age=False).replace('POSTGRES_IMAGE', 'MYSQL_IMAGE')
        (self.install / 'release.env').write_text(legacy)
        result = self.update(*self.bundle())
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('still runs on MySQL', result.stderr)
        self.assertIn('#587', result.stderr)
        self.assertEqual((self.install / 'release.env').read_text(), legacy)
        self.assertFalse((self.install / 'backups').exists())
        self.assertFalse((self.install / 'backup.sh').exists())

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
        self.assertNotIn('postgres', up)
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
        result = self.update(*self.bundle(), extra={'FAKE_STORE_DNS_MISSING': '1'})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Point a DNS record for store.example.invalid', result.stderr)
        self.assertEqual((self.install / 'version').read_text().strip(), 'server-v0.1.2')

    def test_unhealthy_public_api_prevents_update_completion(self):
        result = self.update(*self.bundle(), extra={'FAKE_API_HEALTH': 'unexpected response'})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Public route validation failed', result.stderr)
        self.assertEqual((self.install / 'version').read_text().strip(), 'server-v0.1.1')

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
        changed_volumes['volumes'] = {'postgres-data': {'external': True}}
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
        self.assertEqual((self.install / 'release.env').read_text(), manifest('1', store=False, age=False))

    def test_compose_infrastructure_change_preserves_installation(self):
        result = self.update(*self.bundle(compose_file=compose(postgres_image='postgres-next')))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('changes postgres', result.stderr)
        self.assertEqual((self.install / 'release.env').read_text(), manifest('1', store=False, age=False))
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

    def test_removes_unused_untagged_runner_images_before_the_disk_check(self):
        log = self.root / 'docker.log'
        repository = 'example.invalid/runner'  # the fixture manifest pins RUNNER_IMAGE in this repository
        # Listed oldest first on purpose: the updater must sort by creation time, keep the newest
        # untagged image for rollback and never remove the pinned one (the fake resolves pins to sha256:pinned).
        listing = ';'.join([f'sha256:old2 {repository} <none>', f'sha256:pinned {repository} <none>',
                            f'sha256:old1 {repository} <none>', f'sha256:new {repository} <none>',
                            f'sha256:tagged {repository} latest', 'sha256:other example.invalid/other <none>'])
        created = 'sha256:old2=2026-10-01T00:00:00Z;sha256:pinned=2026-10-15T00:00:00Z;sha256:old1=2026-10-20T00:00:00Z;sha256:new=2026-11-01T00:00:00Z'
        result = self.update(*self.bundle(), extra={'FAKE_DOCKER_LOG': str(log), 'FAKE_IMAGE_LS': listing, 'FAKE_IMAGE_CREATED': created})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        removed = [args[-1] for args in calls if args[:2] == ['image', 'rm'] and args[-1].startswith('sha256:')]
        self.assertEqual(removed, ['sha256:old1', 'sha256:old2'])

    def test_prunes_unused_docker_data_when_disk_is_short_and_continues(self):
        log = self.root / 'docker.log'
        low = {'FAKE_DOCKER_LOG': str(log), 'FAKE_DF_LOW_KB': '8000000', 'FAKE_PRUNED': str(self.root / 'pruned'), 'FAKE_PRUNE_FREES': '1'}
        result = self.update(*self.bundle(), extra=low)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Freed', result.stdout)
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        self.assertIn(['image', 'prune', '-a', '-f'], calls)
        self.assertIn(['builder', 'prune', '-f'], calls)
        self.assertFalse([c for c in calls if 'volume' in c or c[:2] == ['system', 'prune']])

    def test_still_short_after_pruning_names_what_uses_the_disk(self):
        low = {'FAKE_DF_LOW_KB': '8000000', 'FAKE_PRUNED': str(self.root / 'pruned')}
        result = self.update(*self.bundle(), extra=low)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('At least 10 GiB free disk space is required', result.stderr)
        self.assertIn('TYPE TOTAL ACTIVE SIZE RECLAIMABLE', result.stderr)
        self.assertIn('Backups in', result.stderr)

    def test_does_not_prune_when_enough_space_is_free(self):
        log = self.root / 'docker.log'
        result = self.update(*self.bundle(), extra={'FAKE_DOCKER_LOG': str(log)})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn('prune', log.read_text())

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
        self.assertEqual((self.install / 'release.env').read_text(), manifest('1', store=False, age=False))
        self.assertFalse((self.install / 'backups').exists())

    def test_failed_service_update_keeps_previous_manifest_and_backup(self):
        result = self.update(*self.bundle(), extra={'FAKE_UP_FAIL': '1'})
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.install / 'release.env').read_text(), manifest('1', store=False, age=False))
        backup, = (self.install / 'backups').iterdir()
        self.assertTrue((backup / 'database.dump').is_file())

    def test_preserves_and_uses_reviewed_override_for_both_manifests(self):
        override = self.install / 'compose.override.yaml'
        content = 'services:\n  postgres:\n    image: example.invalid/retained-postgres@sha256:' + '9' * 64 + '\n'
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
