#!/usr/bin/env python3
"""Checks how the VPS is set up: SSH password and root password login, ports open to the internet beyond 22, 80 and 443,
and whether a firewall is present. The installer runs it (interactive offer to fix); the Runner runs it read-only in a
helper container (--json --root /host) so Server Keeper can show the same findings.

Fixes never lock the user out: passwords are only turned off when an authorized key exists for a user who can log in.
Cloud provider firewalls are out of scope."""
import argparse
import glob
import ipaddress
import json
import os
import re
import shutil
import subprocess
import sys

# 5671 is the port Runners connect to; 53 is added by the installer for delegated TLS (--allow-port).
ALLOWED_PORTS = {22, 80, 443, 5671}
DROPIN = '/etc/ssh/sshd_config.d/00-automaton-hardening.conf'


def read(root, path):
    try:
        with open(os.path.join(root, path.lstrip('/')), encoding='utf-8', errors='replace') as handle:
            return handle.read()
    except OSError:
        return None


def sshd_options(root):
    """Effective value of each option: sshd keeps the first value it reads, and Include files are read where they appear."""
    values = {}

    def load(path, depth=0):
        text = read(root, path)
        if text is None or depth > 5:
            return
        for raw in text.splitlines():
            parts = raw.split('#', 1)[0].strip().split(None, 1)
            if len(parts) != 2:
                continue
            key, value = parts[0].lower(), parts[1].strip().strip('"')
            if key == 'include':
                for pattern in value.split():
                    pattern = pattern if pattern.startswith('/') else '/etc/ssh/' + pattern
                    for match in sorted(glob.glob(os.path.join(root, pattern.lstrip('/')))):
                        load('/' + os.path.relpath(match, root), depth + 1)
            elif key not in values:
                values[key] = value.lower()

    load('/etc/ssh/sshd_config')
    return values, read(root, '/etc/ssh/sshd_config') is not None


def password_login(options):
    return options.get('passwordauthentication', 'yes') == 'yes'


def root_password_login(options):
    return options.get('permitrootlogin', 'prohibit-password') in ('yes', 'true')


def users_with_keys(root):
    """Accounts that have an authorized key and a shell: the people who can still log in once passwords are off."""
    found = []
    passwd = read(root, '/etc/passwd') or ''
    for line in passwd.splitlines():
        fields = line.split(':')
        if len(fields) < 7 or fields[6].endswith(('nologin', 'false')):
            continue
        keys = read(root, os.path.join(fields[5], '.ssh/authorized_keys'))
        if keys and any(l.strip() and not l.lstrip().startswith('#') for l in keys.splitlines()):
            found.append(fields[0])
    return found


def hex_address(text, family):
    raw = bytes.fromhex(text)
    if family == 4:
        return str(ipaddress.IPv4Address(raw[::-1]))
    words = [raw[i:i + 4][::-1] for i in range(0, 16, 4)]
    return str(ipaddress.IPv6Address(b''.join(words)))


def public_listeners(proc):
    """TCP ports in LISTEN state on an address the internet can reach (any address or a public one), with the address."""
    ports = {}
    for name, family, tcp in (('tcp', 4, True), ('tcp6', 6, True)):
        try:
            with open(os.path.join(proc, 'net', name), encoding='ascii') as handle:
                rows = handle.read().splitlines()[1:]
        except OSError:
            continue
        for row in rows:
            fields = row.split()
            if len(fields) < 4 or (tcp and fields[3] != '0A'):
                continue
            try:
                address, port = fields[1].split(':')
                address = hex_address(address, family)
                port = int(port, 16)
            except (ValueError, IndexError):
                continue
            ip = ipaddress.ip_address(address)
            if ip.is_unspecified or ip.is_global:
                ports.setdefault(port, ('tcp', address))
    return ports


def firewall(root, live):
    ufw = read(root, '/etc/ufw/ufw.conf')
    if ufw and re.search(r'^ENABLED=yes', ufw, re.M | re.I):
        return 'ufw'
    if os.path.exists(os.path.join(root, 'run/firewalld.pid')):
        return 'firewalld'
    if live:
        # Docker adds its own DROP rules (forward chains), so only an input policy or input hook that drops counts.
        if shutil.which('iptables'):
            result = subprocess.run(['iptables', '-S', 'INPUT'], capture_output=True, text=True)
            if result.returncode == 0 and re.search(r'^-P INPUT (DROP|REJECT)', result.stdout, re.M):
                return 'iptables'
        if shutil.which('nft'):
            result = subprocess.run(['nft', 'list', 'ruleset'], capture_output=True, text=True)
            if result.returncode == 0 and re.search(r'hook input[^\n]*policy drop', result.stdout):
                return 'nftables'
    return None


def ufw_allowed_ports(root):
    """TCP ports ufw lets in, or None when its rules cannot be read or contain rules (source-only, ranges) this cannot reason about."""
    rules = read(root, '/etc/ufw/user.rules')
    if rules is None:
        return None
    ports = set()
    for line in rules.splitlines():
        if not line.startswith('-A ufw-user-input ') or not line.endswith('-j ACCEPT'):
            continue
        match = re.search(r'-p tcp .*--dport (\d+) ', line + ' ')
        if match is None or re.search(r'-s |--dport \d+:', line):
            return None
        ports.add(int(match.group(1)))
    return ports


def docker_published_ports(live):
    """Host ports Docker publishes: they skip ufw, so they stay open to the internet whatever ufw says."""
    if not live or not shutil.which('docker'):
        return set()
    result = subprocess.run(['docker', 'ps', '--format', '{{.Ports}}'], capture_output=True, text=True)
    return {int(m.group(1)) for m in re.finditer(r':(\d+)->', result.stdout)} if result.returncode == 0 else set()


def check(root='/', proc='/proc', live=True, allowed=(), docker_ports=()):
    options, has_config = sshd_options(root)
    keys = [user for user in users_with_keys(root) if user in ('root', os.environ.get('SUDO_USER'))]
    published = docker_published_ports(live) | set(docker_ports)
    blocked = ufw_allowed_ports(root) if firewall(root, False) == 'ufw' else None
    ports = sorted((port, kind, address) for port, (kind, address) in public_listeners(proc).items() if port not in ALLOWED_PORTS | set(allowed) and (blocked is None or port in blocked or port in published))
    found = []
    if has_config and password_login(options):
        found.append({'id': 'ssh-password', 'title': 'SSH allows passwords',
                      'detail': 'Anyone on the internet can guess passwords. Use SSH keys and turn password login off.',
                      'fixable': bool(keys)})
    if has_config and root_password_login(options):
        found.append({'id': 'ssh-root-password', 'title': 'SSH allows root to log in with a password',
                      'detail': 'Root can sign in with a password. Allow root with a key only.', 'fixable': bool(keys)})
    for port, kind, address in ports[:50]:
        found.append({'id': f'port-{kind}-{port}', 'title': f'Port {port}/{kind} is open to the internet',
                      'detail': f'Listening on {address}; only 22, 80, 443 and 5671 (Runners) are needed. Turn on the firewall, or bind it to 127.0.0.1 (a port Docker publishes bypasses ufw).',
                      'fixable': False})
    provider = firewall(root, live)
    # The read-only helper cannot see iptables or nftables rules, so there "no firewall" would be a guess: it only reports it when it can look.
    if provider is None and live:
        found.append({'id': 'no-firewall', 'title': 'No firewall found on this server',
                      'detail': 'Turn on a host firewall that allows only 22, 80, 443 and 5671 (a cloud provider firewall is not checked).',
                      'fixable': shutil.which('ufw') is not None})
    return {'findings': found, 'firewall': provider, 'authorizedKeyUsers': len(keys), 'sshConfigRead': has_config}


def has_tty():
    try:
        open('/dev/tty').close()
        return True
    except OSError:
        return False


def ask(question, default_yes=True):
    suffix = '[Y/n]' if default_yes else '[y/N]'
    try:
        # /dev/tty is not seekable, so a 'r+' text file fails; use separate handles.
        # Also works when the script itself was piped into bash.
        with open('/dev/tty', 'w') as out, open('/dev/tty', 'r') as inp:
            out.write(f'{question} {suffix} ')
            out.flush()
            line = inp.readline()
            if not line:  # Ctrl-D is not a yes
                return False
            answer = line.strip().lower()
    except OSError:
        return False
    return default_yes if not answer else answer.startswith('y')


def run(command):
    return subprocess.run(command, capture_output=True, text=True).returncode == 0


def fix_ssh(ids):
    lines = []
    if 'ssh-password' in ids:
        lines.append('PasswordAuthentication no')
    if 'ssh-root-password' in ids:
        lines.append('PermitRootLogin prohibit-password')
    if not re.search(r'^\s*include\s+.*sshd_config\.d', read('/', '/etc/ssh/sshd_config') or '', re.M | re.I):
        return False
    previous = None
    if os.path.exists(DROPIN):
        previous = open(DROPIN).read()
    os.makedirs(os.path.dirname(DROPIN), mode=0o755, exist_ok=True)
    with open(DROPIN, 'w') as handle:
        handle.write('# Written by the Automaton installer; delete this file to allow passwords again.\n' + '\n'.join(lines) + '\n')
    sshd = shutil.which('sshd') or '/usr/sbin/sshd'
    if not run([sshd, '-t']):
        os.remove(DROPIN) if previous is None else open(DROPIN, 'w').write(previous)
        return False
    reloaded = run(['systemctl', 'reload', 'ssh']) or run(['systemctl', 'reload', 'sshd'])
    # Only claim it worked when the running configuration now says so.
    after, _ = sshd_options('/')
    return reloaded and ('ssh-password' not in ids or not password_login(after)) and ('ssh-root-password' not in ids or not root_password_login(after))


def ssh_ports():
    """Every port sshd listens on (sshd -T reads drop-ins too) plus the port of the session running this installer."""
    ports = set()
    sshd = shutil.which('sshd') or '/usr/sbin/sshd'
    result = subprocess.run([sshd, '-T'], capture_output=True, text=True) if os.path.exists(sshd) else None
    if result and result.returncode == 0:
        ports |= {int(m.group(1)) for m in re.finditer(r'^port (\d+)$', result.stdout, re.M)}
        ports |= {int(m.group(1)) for m in re.finditer(r'^listenaddress .*:(\d+)$', result.stdout, re.M)}
    parts = os.environ.get('SSH_CONNECTION', '').split()
    if len(parts) == 4 and parts[3].isdigit():
        ports.add(int(parts[3]))
    return ports


def fix_firewall():
    ports = ssh_ports()
    if not ports:
        print('  Not changed: could not find the port SSH uses, and turning on the firewall could cut you off.')
        return False
    ports = sorted(ports) + [80, 443, 5671] + list(ALLOW)
    steps = [['ufw', '--force', 'default', 'deny', 'incoming']] + [['ufw', 'allow', f'{p}/tcp'] for p in ports] + [['ufw', '--force', 'enable']]
    return all(run(step) for step in steps)


ALLOW = []


def interactive():
    result = check(allowed=ALLOW)
    if not result['findings']:
        print('Host security: SSH passwords are off, only 22, 80, 443 and 5671 are open and a firewall is present.')
        return 0
    interactive_ok = '--yes' not in sys.argv and has_tty()
    print('Host security: this server needs you:')
    for item in result['findings']:
        print(f"  - {item['title']}. {item['detail']}")
    if not interactive_ok:
        print('Nothing was changed (no prompt available). Run: sudo python3 host_security.py   to fix these.')
        return 0
    ssh_ids = [i['id'] for i in result['findings'] if i['id'].startswith('ssh-') and i['fixable']]
    prompts = {'ssh-password': 'SSH allows passwords. Turn it off? (your SSH key login keeps working)',
               'ssh-root-password': 'Root can log in with a password. Allow root with a key only?'}
    for item in result['findings']:
        if item['id'].startswith('ssh-') and not item['fixable']:
            print(f"  Not changed: {item['title']} - no account has an authorized SSH key, so turning passwords off would lock you out. Add a key first.")
    chosen = [i for i in ssh_ids if ask(prompts[i])]
    if chosen:
        print('  SSH hardened.' if fix_ssh(chosen) else '  SSH was not changed (sshd_config does not read /etc/ssh/sshd_config.d, or the new setting did not validate).')
    if any(i['id'] == 'no-firewall' and i['fixable'] for i in result['findings']) and ask(
            'No firewall. Turn on ufw allowing only 22, 80, 443 and 5671 (Runners)?'):
        print('  Firewall enabled.' if fix_firewall() else '  The firewall could not be enabled.')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--json', action='store_true', help='print the findings as JSON and change nothing')
    parser.add_argument('--root', default='/', help='where the host filesystem is mounted')
    parser.add_argument('--proc', default='/proc', help='the /proc to read listening ports from')
    parser.add_argument('--yes', action='store_true', help='only warn; never change anything')
    parser.add_argument('--docker-port', type=int, action='append', default=[], help='a host port Docker publishes (ufw does not filter it)')
    parser.add_argument('--allow-port', type=int, action='append', default=[], help='another port that is meant to be open')
    args = parser.parse_args()
    ALLOW[:] = args.allow_port
    if args.json:
        result = check(args.root, args.proc, live=args.root == '/', allowed=args.allow_port, docker_ports=args.docker_port)
        print(json.dumps(result))
        return 0 if result['sshConfigRead'] else 3  # unknown, never "all clear", when sshd_config cannot be read
    return interactive()


if __name__ == '__main__':
    sys.exit(main())
