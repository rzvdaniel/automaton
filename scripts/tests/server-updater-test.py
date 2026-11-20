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
elif args[0]=='ps':
    if os.environ.get('FAKE_WEBSITE_HOST')=='1' and 'label=com.docker.compose.service=website-green' in args: print('abc123')
elif args[0]=='compose':
    if 'config' in args and '--format' in args: print(open(args[args.index('-f')+1]).read())
    elif 'config' in args and '--services' in args: print(*json.load(open(args[args.index('-f')+1]))['services'], sep=chr(10))
    elif 'ps' in args: print('container-'+args[-1])
    elif 'exec' in args:
        # The updater runs pg_dump (custom format) and pg_restore --list in the postgres service.
        if args[args.index('exec')+1:][:2]!=['-T','postgres']: sys.exit(2)
        command=args[args.index('postgres')+1:]
        if command[0]=='psql' and '-c' in command and 'DROP SCHEMA public CASCADE' in command[command.index('-c')+1]:
            open(os.environ['FAKE_STATE']+'.schema-reset','w').close()
        elif command[0]=='psql':
            print(os.environ.get('FAKE_MIGRATIONS_AFTER','5') if os.environ.get('FAKE_UP_FAIL') and os.path.exists(os.environ['FAKE_STATE']) else '5')
        elif command[0]=='pg_restore' and '--single-transaction' in command and '--exit-on-error' in command and '--clean' not in command:
            if sys.stdin.buffer.read()!=b'PGDMP-fake-custom-archive': sys.exit(1)
            # A table the new migration added would make the restore fail unless the schema was emptied first.
            if os.environ.get('FAKE_MIGRATION_ADDS_TABLE')=='1' and not os.path.exists(os.environ['FAKE_STATE']+'.schema-reset'): sys.exit(1)
        elif command[0]=='pg_dump':
            if '--format=custom' not in command or command[command.index('-d')+1]!='automaton': sys.exit(2)
            sys.stdout.buffer.write(b'PGDMP-fake-custom-archive')
        elif command[:2]==['pg_restore','--list']:
            if sys.stdin.buffer.read()!=b'PGDMP-fake-custom-archive': sys.exit(1)
            print('; Archive created by fake pg_dump')
        else: sys.exit(2)
    elif 'up' in args:
        if os.environ.get('FAKE_UP_FAIL')=='1' and not os.path.exists(os.environ['FAKE_STATE']):
            open(os.environ['FAKE_STATE'],'w').close(); sys.exit(1)
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
        # visudo -cf: fail on request, otherwise accept only the one expected line.
        (self.bin / 'visudo').write_text('''#!/bin/sh
[ "$FAKE_VISUDO_FAIL" != 1 ] || { echo "visudo: syntax error" >&2; exit 1; }
grep -Eq '^[a-z_][a-z0-9_-]* ALL=\\(root\\) NOPASSWD: /usr/local/bin/automaton-deploy-website$' "$2"
''')
        for tool in self.bin.iterdir():
            tool.chmod(0o755)
        self.update_dir = self.root / 'update'
        self.systemd_dir = self.root / 'systemd'
        self.systemd_dir.mkdir()
        # Never touch the host's systemd units or /var/lib/automaton.
        self.environment = os.environ | {'PATH': str(self.bin) + ':' + os.environ['PATH'],
                                         'AUTOMATON_UPDATE_TEST': '1', 'FAKE_STATE': str(self.root / 'up-failed'), 'AUTOMATON_UPDATE_DIR': str(self.update_dir),
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
        shutil.copytree(REPO / 'docker/production-host', source / 'production-host', dirs_exist_ok=True)
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

    def test_sudo_from_a_limited_account_refuses_paths_and_bundles(self):
        archive, checksum = self.bundle()
        result = self.update(archive, checksum, extra={'SUDO_USER': 'no-such-limited-account'})
        self.assertEqual(result.returncode, 1)
        self.assertIn('root shell', result.stderr)
        self.assertFalse((self.install / 'backups').exists())
        # a root shell that came through `sudo -i` as root is not limited
        result = self.update(archive, checksum, extra={'SUDO_USER': 'root'})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_sudo_from_an_administrator_may_use_bundles(self):
        archive, checksum = self.bundle()
        stub = self.bin / 'sudo'
        stub.write_text('#!/bin/sh\necho "    (ALL : ALL) ALL"\n')
        stub.chmod(0o755)
        result = self.update(archive, checksum, extra={'SUDO_USER': 'an-administrator'})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_sudo_from_a_limited_account_refuses_allow_downgrade(self):
        result = subprocess.run(['bash', str(REPO / 'scripts/update-automaton.sh'), '--allow-downgrade'],
                                env=self.environment | {'SUDO_USER': 'no-such-limited-account'}, capture_output=True, text=True)
        self.assertEqual(result.returncode, 1)
        self.assertIn('root shell', result.stderr)

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

    def test_cleanup_script_removes_the_old_host_timer_and_nothing_else(self):
        script = REPO / 'docker/install/cleanup.sh'
        (self.systemd_dir / 'automaton-cleanup.timer').write_text('[Timer]\n')
        (self.systemd_dir / 'automaton-cleanup.service').write_text('[Service]\n')
        (self.systemd_dir / 'automaton-backup.timer').write_text('[Timer]\n')
        log = self.root / 'cleanup.log'
        environment = self.environment | {'FAKE_DOCKER_LOG': str(log)}
        for _ in range(2):  # safe to repeat
            result = subprocess.run(['bash', str(script), '--install-dir', str(self.install), '--remove-timer'],
                                    env=environment, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.systemd_dir / 'automaton-cleanup.timer').exists())
        self.assertFalse((self.systemd_dir / 'automaton-cleanup.service').exists())
        self.assertTrue((self.systemd_dir / 'automaton-backup.timer').exists())
        self.assertFalse(log.exists() and 'prune' in log.read_text(), 'the host script no longer cleans anything itself')

    def test_update_removes_the_old_cleanup_timer(self):
        (self.systemd_dir / 'automaton-cleanup.timer').write_text('[Timer]\n')
        result = self.update(*self.bundle())
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.install / 'cleanup.sh').stat().st_mode & 0o777, 0o700)
        self.assertFalse((self.systemd_dir / 'automaton-cleanup.timer').exists())

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
        self.assertIn('public route validation failed', result.stderr)
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

    def test_failed_update_rolls_back_and_restores_database_when_a_migration_ran(self):
        log = self.root / 'docker.log'
        result = self.update(*self.bundle(), extra={'FAKE_UP_FAIL': '1', 'FAKE_MIGRATIONS_AFTER': '6', 'FAKE_DOCKER_LOG': str(log)})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Rolled back: the server answers on server-v0.1.1', result.stderr)
        self.assertIn('Database restored: yes', result.stderr)
        self.assertIn('pg_restore', log.read_text())
        self.assertEqual((self.install / 'version').read_text().strip(), 'server-v0.1.1')

    def test_rollback_restores_into_a_clean_schema_when_the_migration_added_a_table(self):
        result = self.update(*self.bundle(), extra={'FAKE_UP_FAIL': '1', 'FAKE_MIGRATIONS_AFTER': '6', 'FAKE_MIGRATION_ADDS_TABLE': '1'})
        self.assertIn('Database restored: yes', result.stderr)
        self.assertNotIn('Database restore failed', result.stderr)

    def test_failed_update_rolls_back_without_database_restore_when_no_migration_ran(self):
        result = self.update(*self.bundle(), extra={'FAKE_UP_FAIL': '1', 'FAKE_MIGRATIONS_AFTER': '5'})
        self.assertIn('Rolled back: the server answers on server-v0.1.1', result.stderr)
        self.assertIn('Database restored: no', result.stderr)

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

    def website_host(self, authorized_keys_line='restrict,command="/usr/local/bin/automaton-deploy" ssh-ed25519 AAAA deploy-production', forced_command=True):
        """A temporary host root with a deploy account, and an existing forced-command script on request."""
        host = self.root / 'host'
        home = self.root / 'home/deployer'
        (home / '.ssh').mkdir(parents=True, exist_ok=True)
        (home / '.ssh/authorized_keys').write_text(authorized_keys_line + '\n')
        other = self.root / 'home/other'
        other.mkdir(parents=True, exist_ok=True)
        passwd = self.root / 'passwd'
        passwd.write_text(f'root:x:0:0::/root:/bin/bash\ndeployer:x:1001:1001::{home}:/bin/bash\nother:x:1002:1002::{other}:/bin/bash\n')
        (host / 'usr/local/bin').mkdir(parents=True, exist_ok=True)
        if forced_command:
            (host / 'usr/local/bin/automaton-deploy').write_text('#!/bin/sh\necho old\n')
        return host, self.environment | {'AUTOMATON_HOST_ROOT': str(host), 'AUTOMATON_PASSWD_FILE': str(passwd), 'FAKE_WEBSITE_HOST': '1'}

    def installed(self, host):
        return {name: (host / name).stat() for name in (
            'usr/local/bin/automaton-deploy-website', 'usr/local/lib/automaton/docker-compose.website.yml',
            'etc/sudoers.d/automaton-deploy-website', 'usr/local/bin/automaton-deploy')}

    def test_website_host_installs_the_deploy_step_from_the_verified_bundle(self):
        host, environment = self.website_host()
        result = self.update(*self.bundle(), extra=environment)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        files = {name: stat.st_mode & 0o777 for name, stat in self.installed(host).items()}
        self.assertEqual(files, {'usr/local/bin/automaton-deploy-website': 0o755, 'usr/local/lib/automaton/docker-compose.website.yml': 0o644,
                                 'etc/sudoers.d/automaton-deploy-website': 0o440, 'usr/local/bin/automaton-deploy': 0o755})
        for stat in self.installed(host).values():
            self.assertEqual((stat.st_uid, stat.st_gid), (0, 0))
        self.assertEqual((host / 'usr/local/bin/automaton-deploy-website').read_bytes(), (REPO / 'docker/production-host/automaton-deploy-website').read_bytes())
        self.assertEqual((host / 'etc/sudoers.d/automaton-deploy-website').read_text(),
                         'deployer ALL=(root) NOPASSWD: /usr/local/bin/automaton-deploy-website\n')
        forced = (host / 'usr/local/bin/automaton-deploy').read_text()
        self.assertIn(r'^sudo\ /usr/bin/bash\ /opt/automaton-server/update\.sh\ --version\ server-v[0-9]+\.[0-9]+\.[0-9]+$', forced)
        self.assertIn(r'^sudo\ /usr/local/bin/automaton-deploy-website\ [0-9]+\.[0-9]+\.[0-9]+$', forced)
        self.assertEqual(sorted(path.name for path in (host / 'usr/local/bin').iterdir()), ['automaton-deploy', 'automaton-deploy-website'])
        self.assertEqual(sorted(path.name for path in (host / 'etc/sudoers.d').iterdir()), ['automaton-deploy-website'])

    def test_forced_command_is_only_rewritten_when_it_exists(self):
        host, environment = self.website_host(forced_command=False)
        result = self.update(*self.bundle(), extra=environment)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue((host / 'usr/local/bin/automaton-deploy-website').is_file())
        self.assertFalse((host / 'usr/local/bin/automaton-deploy').exists())

    def test_normal_host_gets_nothing_new(self):
        host, environment = self.website_host()
        result = self.update(*self.bundle(), extra=environment | {'FAKE_WEBSITE_HOST': '0'})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((host / 'usr/local/bin/automaton-deploy').read_text(), '#!/bin/sh\necho old\n')
        self.assertEqual([path.name for path in (host / 'usr/local/bin').iterdir()], ['automaton-deploy'])
        self.assertFalse((host / 'etc').exists())
        self.assertFalse((host / 'usr/local/lib').exists())

    def test_website_deploy_setup_is_idempotent(self):
        host, environment = self.website_host()
        archive, checksum = self.bundle()
        self.assertEqual(self.update(archive, checksum, extra=environment).returncode, 0)
        before = {name: (stat.st_mtime_ns, stat.st_ino) for name, stat in self.installed(host).items()}
        (self.install / 'release.env').write_text(manifest('2'))  # same release again: the re-run path
        result = self.update(archive, checksum, extra=environment)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn('Installed ', result.stdout)
        self.assertEqual(before, {name: (stat.st_mtime_ns, stat.st_ino) for name, stat in self.installed(host).items()})

    def test_stale_website_files_are_replaced(self):
        host, environment = self.website_host()
        (host / 'usr/local/bin/automaton-deploy-website').write_text('old')
        result = self.update(*self.bundle(), extra=environment)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Installed ' + str(host / 'usr/local/bin/automaton-deploy-website'), result.stdout)
        self.assertEqual((host / 'usr/local/bin/automaton-deploy-website').read_bytes(), (REPO / 'docker/production-host/automaton-deploy-website').read_bytes())

    def test_sudoers_that_fails_validation_is_not_installed_and_the_forced_command_is_kept(self):
        host, environment = self.website_host()
        result = self.update(*self.bundle(), extra=environment | {'FAKE_VISUDO_FAIL': '1'})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)  # the update itself still completes
        self.assertIn('did not validate', result.stderr)
        self.assertEqual((self.install / 'version').read_text().strip(), 'server-v0.1.2')
        self.assertEqual(list((host / 'etc/sudoers.d').iterdir()), [])
        self.assertEqual((host / 'usr/local/bin/automaton-deploy').read_text(), '#!/bin/sh\necho old\n')

    def test_no_single_deploy_account_means_no_sudoers_line(self):
        host, environment = self.website_host(authorized_keys_line='ssh-ed25519 AAAA ordinary-key')
        result = self.update(*self.bundle(), extra=environment)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('No single account', result.stderr)
        self.assertEqual(list((host / 'etc/sudoers.d').iterdir()), [])
        self.assertEqual((host / 'usr/local/bin/automaton-deploy').read_text(), '#!/bin/sh\necho old\n')

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
