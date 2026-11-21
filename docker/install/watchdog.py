#!/usr/bin/env python3
"""Tell the administrator within minutes when Automaton's containers, data or backup are missing.

Runs on the host (not in a container) every 5 minutes from automaton-watchdog.timer, so it still works when the
server is down. Checks: containers exist and run, data volumes exist, Studio answers, the last backup succeeded and is
under 26 hours old. Sends a webhook (Slack, Discord, Telegram or any URL) and/or an email straight through the SMTP
set in appsettings.Production.json, never through Automaton.

  watchdog.py [--install-dir DIR]     run the checks once
  watchdog.py --install-units         install and start the timer
  watchdog.py --test                  send a test message to every configured destination

Alert destinations: $CONFIG_DIR/alerts.json (root only), for example
  {"webhookUrl": "https://hooks.slack.com/services/...", "emailTo": "admin@example.com"}
Without "emailTo" the email goes to EmailConfig.AdminEmail. Without any destination, problems are only written to the
journal (sudo journalctl -u automaton-watchdog.service).

If containers are missing or stopped but every data volume exists, they are started again with `docker compose up -d`.
If a volume is missing nothing is started (that would create an empty database); the alert names the restore command.
"""
import argparse
import json
import os
import smtplib
import ssl
import subprocess
import sys
import time
import urllib.request
from datetime import datetime, timezone
from email.message import EmailMessage

MAX_BACKUP_AGE_HOURS = 26
REMIND_AFTER_SECONDS = 6 * 3600
DEFAULT_SERVICES = ['postgres', 'rabbitmq', 'api', 'worker', 'studio', 'traefik']
DEFAULT_VOLUMES = ['postgres-data', 'rabbitmq-data', 'api-data', 'api-keys', 'worker-keys', 'certificates']


def run(command, timeout=120):
    try:
        done = subprocess.run(command, capture_output=True, text=True, timeout=timeout)
        return done.returncode, done.stdout
    except (OSError, subprocess.TimeoutExpired):
        return 1, ''


def read_json(path, default=None):
    try:
        with open(path) as stream:
            return json.load(stream)
    except (OSError, ValueError):
        return default


def read_env(path, key):
    try:
        for line in open(path):
            if line.startswith(key + '='):
                return line.split('=', 1)[1].strip()
    except OSError:
        pass
    return ''


class Host:
    def __init__(self, install_dir, state_dir):
        self.install_dir = install_dir
        self.state_dir = state_dir
        self.config_dir = read_env(os.path.join(install_dir, '.env'), 'CONFIG_DIR') or '/etc/automaton'
        self.settings = read_json(os.path.join(self.config_dir, 'appsettings.Production.json'), {}) or {}
        self.alerts = read_json(os.path.join(self.config_dir, 'alerts.json'), {}) or {}
        self.compose = ['docker', 'compose', '--project-directory', install_dir,
                        '--env-file', os.path.join(install_dir, 'release.env'), '--env-file', os.path.join(install_dir, '.env'),
                        '-f', os.path.join(install_dir, 'compose.yaml')]
        override = os.path.join(install_dir, 'compose.override.yaml')
        if os.path.isfile(override):
            self.compose += ['-f', override]

    def domain(self):
        api = (self.settings.get('AppConfig') or {}).get('ApiUrl', '')
        host = api.split('://', 1)[-1].split('/', 1)[0]
        return host[4:] if host.startswith('api.') else host

    def names(self, what, default):
        code, out = run(self.compose + ['config', f'--{what}'])
        listed = [line.strip() for line in out.splitlines() if line.strip()]
        return listed if code == 0 and listed else default

    def containers(self):
        """Maps each Compose service of the server to its container state."""
        code, out = run(['docker', 'ps', '-a', '--filter', 'label=com.docker.compose.project=automaton-server', '--format',
                         '{{.Label "com.docker.compose.service"}} {{.State}}'])
        if code != 0:
            return None
        states = {}
        for line in out.splitlines():
            parts = line.split()
            if len(parts) == 2:
                states[parts[0]] = parts[1]
        return states

    def volumes(self):
        code, out = run(['docker', 'volume', 'ls', '-q', '--filter', 'label=com.docker.compose.project=automaton-server',
                         '--format', '{{.Label "com.docker.compose.volume"}}'])
        return None if code != 0 else {line.strip() for line in out.splitlines() if line.strip()}

    def busy(self):
        """An update, restore, restart or backup is running: it stops containers on purpose."""
        for path in (os.environ.get('AUTOMATON_INSTALL_LOCK_FILE', '/run/lock/automaton-install.lock'),
                     os.environ.get('AUTOMATON_BACKUP_LOCK_FILE', '/run/lock/automaton-backup.lock')):
            if not os.path.exists(path):
                continue
            code, _ = run(['flock', '-n', path, 'true'], 10)
            if code != 0:
                return True
        return False

    def studio_answers(self):
        domain = self.domain()
        if not domain:
            return True
        for _ in range(3):
            try:
                with urllib.request.urlopen(f'https://studio.{domain}/', timeout=15):
                    return True
            except urllib.error.HTTPError:
                return True  # the web server itself answered
            except (OSError, ValueError):
                time.sleep(3)
        return False

    def latest_backup(self):
        names = sorted((n for n in os.listdir(os.path.join(self.install_dir, 'backups', 'nightly'))
                        if n.startswith('automaton-') and not n.endswith('.age')), reverse=True) \
            if os.path.isdir(os.path.join(self.install_dir, 'backups', 'nightly')) else []
        return names[0] if names else None


def check_containers(host, services, volumes_ok):
    states = host.containers()
    if states is None:
        return None
    missing = [s for s in services if s not in states]
    stopped = [s for s in services if s in states and states[s] != 'running']
    if not missing and not stopped:
        return None
    parts = []
    if missing:
        parts.append(', '.join(missing) + ' missing')
    if stopped:
        parts.append(', '.join(stopped) + ' stopped')
    problem = 'Automaton is down: ' + '; '.join(parts)
    if not volumes_ok:
        return problem + '. Nothing was started because the data volumes are missing too.'
    code, _ = run(host.compose + ['up', '-d', '--wait', '--wait-timeout', '240'], 300)
    again = host.containers() or {}
    if code == 0 and all(again.get(s) == 'running' for s in services):
        return problem + '. Started again with docker compose up -d; it is back.'
    return problem + '. Starting it again with docker compose up -d did not work; check: sudo docker compose ps'


def check_volumes(host, expected):
    existing = host.volumes()
    if existing is None:
        return None, True
    missing = [v for v in expected if v not in existing]
    if not missing:
        return None, True
    backup = host.latest_backup() or '<backup name from: sudo bash %s/backup.sh --list>' % host.install_dir
    command = f'sudo bash {host.install_dir}/restore.sh {backup} --confirm {host.domain() or "<your-domain>"}'
    return ("Automaton's data volumes are missing (" + ', '.join(missing) + '): restore from backup. Run: ' + command), False


def check_backup(host):
    status = read_json(os.path.join(host.config_dir, 'status', 'backup.json'))
    if status is None:
        try:
            age = time.time() - os.path.getmtime(os.path.join(host.install_dir, '.env'))
        except OSError:
            return None
        if age > MAX_BACKUP_AGE_HOURS * 3600:
            return 'No backup has been taken yet. Run: sudo bash %s/backup.sh' % host.install_dir
        return None
    if status.get('lastError'):
        return 'Last backup failed (%s)' % status['lastError']
    success = status.get('lastSuccessAt')
    if not success:
        return None
    try:
        taken = datetime.strptime(success, '%Y-%m-%dT%H:%M:%SZ').replace(tzinfo=timezone.utc)
    except ValueError:
        return None
    hours = (datetime.now(timezone.utc) - taken).total_seconds() / 3600
    if hours > MAX_BACKUP_AGE_HOURS:
        return 'Last backup is %d h old (taken %s)' % (hours, success)
    return None


def send_webhook(url, subject, text):
    body = json.dumps({'text': subject + '\n' + text, 'content': subject + '\n' + text}).encode()
    request = urllib.request.Request(url, data=body, headers={'Content-Type': 'application/json'}, method='POST')
    urllib.request.urlopen(request, timeout=20).close()


def send_email(host, recipient, subject, text):
    config = host.settings.get('EmailConfig') or {}
    server = config.get('EmailHost') or ''
    if not server or not recipient:
        raise RuntimeError('SMTP host or recipient is not configured')
    port = int(config.get('EmailPort') or 587)
    message = EmailMessage()
    message['Subject'] = subject
    message['From'] = config.get('FromEmail') or config.get('EmailAccount') or 'automaton@localhost'
    message['To'] = recipient
    message.set_content(text)
    if config.get('EmailUseSsl') or port == 465:
        smtp = smtplib.SMTP_SSL(server, port, timeout=30, context=ssl.create_default_context())
    else:
        smtp = smtplib.SMTP(server, port, timeout=30)
    with smtp:
        if not (config.get('EmailUseSsl') or port == 465):
            try:
                smtp.starttls(context=ssl.create_default_context())
            except smtplib.SMTPNotSupportedError:
                pass
        if config.get('EmailAccount') and config.get('EmailPassword'):
            try:
                smtp.login(config['EmailAccount'], config['EmailPassword'])
            except smtplib.SMTPNotSupportedError:
                pass
        smtp.send_message(message)


def notify(host, subject, text):
    """Returns the number of destinations that got the message."""
    sent = 0
    if host.alerts.get('webhookUrl'):
        try:
            send_webhook(host.alerts['webhookUrl'], subject, text)
            sent += 1
        except (OSError, ValueError) as error:
            print(f'Webhook failed: {type(error).__name__}', file=sys.stderr)
    recipient = host.alerts.get('emailTo') or (host.settings.get('EmailConfig') or {}).get('AdminEmail') or ''
    if recipient:
        try:
            send_email(host, recipient, subject, text)
            sent += 1
        except (OSError, ValueError, RuntimeError, smtplib.SMTPException) as error:
            print(f'Email failed: {type(error).__name__}', file=sys.stderr)
    return sent


def check_once(host):
    if host.busy():
        print('An update, restore or backup is running; checks skipped.')
        return 0
    services = host.names('services', DEFAULT_SERVICES)
    volume_problem, volumes_ok = check_volumes(host, host.names('volumes', DEFAULT_VOLUMES))
    problems = {}
    if volume_problem:
        problems['volumes'] = volume_problem
    container_problem = check_containers(host, services, volumes_ok)
    if container_problem:
        problems['containers'] = container_problem
    if not container_problem or 'it is back' in container_problem:
        if not host.studio_answers():
            problems['studio'] = "Automaton isn't answering at https://studio.%s/" % host.domain()
    backup_problem = check_backup(host)
    if backup_problem:
        problems['backup'] = backup_problem

    state_path = os.path.join(host.state_dir, 'state.json')
    state = read_json(state_path, {}) or {}
    now = time.time()
    name = host.domain() or os.uname().nodename
    for key, problem in problems.items():
        print(problem)
        # "Started again" outcomes are one-off events, not a lasting problem, so they are not repeated.
        recovered_now = 'it is back' in problem
        stable = problem.split('. Started again')[0] if recovered_now else problem
        previous = state.get(key) or {}
        if previous.get('problem') != stable or (not recovered_now and now - previous.get('at', 0) > REMIND_AFTER_SECONDS):
            if notify(host, f'[Automaton] {name}: ' + problem.split(':')[0].split('. ')[0][:80], problem) or not (
                    host.alerts.get('webhookUrl') or host.alerts.get('emailTo')
                    or (host.settings.get('EmailConfig') or {}).get('AdminEmail')):
                state[key] = {'problem': stable, 'at': now} if not recovered_now else {'problem': None, 'at': now}
    for key in [k for k in state if k not in problems]:
        if state[key].get('problem'):
            notify(host, f'[Automaton] {name}: recovered ({key})', f'The {key} problem is gone: {state[key]["problem"]}')
        del state[key]
    os.makedirs(host.state_dir, mode=0o700, exist_ok=True)
    temporary = state_path + '.tmp'
    with open(temporary, 'w') as stream:
        json.dump(state, stream)
    os.replace(temporary, state_path)
    return 1 if problems else 0


def install_units(install_dir):
    unit_dir = os.environ.get('AUTOMATON_SYSTEMD_DIR', '/etc/systemd/system')
    script = os.path.join(install_dir, 'watchdog.py')
    files = {
        'automaton-watchdog.service': f"""[Unit]
Description=Check that Automaton's containers, data and backup are present
After=docker.service

[Service]
Type=oneshot
ExecStart=/usr/bin/env python3 {script} --install-dir {install_dir}
SuccessExitStatus=0 1
""",
        'automaton-watchdog.timer': """[Unit]
Description=Run the Automaton watchdog every 5 minutes

[Timer]
OnBootSec=2min
OnUnitActiveSec=5min
AccuracySec=30s

[Install]
WantedBy=timers.target
""",
    }
    for name, content in files.items():
        with open(os.path.join(unit_dir, name), 'w') as stream:
            stream.write(content)
        os.chmod(os.path.join(unit_dir, name), 0o644)
    if not os.environ.get('AUTOMATON_SYSTEMD_DIR'):
        subprocess.run(['systemctl', 'daemon-reload'], check=True)
        subprocess.run(['systemctl', 'enable', '--now', 'automaton-watchdog.timer'], check=True, stdout=subprocess.DEVNULL)
    print('Watchdog enabled (automaton-watchdog.timer): checks every 5 minutes.')


def main():
    parser = argparse.ArgumentParser(description='Alert when Automaton is down, its data is missing or its backup is stale.')
    parser.add_argument('--install-dir', default='/opt/automaton-server')
    parser.add_argument('--state-dir', default=os.environ.get('AUTOMATON_WATCHDOG_STATE_DIR', '/var/lib/automaton/watchdog'))
    parser.add_argument('--install-units', action='store_true')
    parser.add_argument('--test', action='store_true')
    args = parser.parse_args()
    if os.geteuid() != 0 and not os.environ.get('AUTOMATON_WATCHDOG_ALLOW_USER'):
        sys.exit('Run as root (sudo).')
    if args.install_units:
        install_units(args.install_dir)
        return 0
    host = Host(args.install_dir, args.state_dir)
    if args.test:
        count = notify(host, '[Automaton] test alert', 'This is a test message from the Automaton watchdog.')
        print(f'Test message sent to {count} destination(s).')
        return 0 if count else 1
    return check_once(host)


if __name__ == '__main__':
    sys.exit(main())
