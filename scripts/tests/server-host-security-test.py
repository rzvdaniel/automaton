#!/usr/bin/env python3
"""The host security check finds SSH password login, public ports and a missing firewall, and never offers a fix that locks the user out."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('host_security', Path(__file__).resolve().parents[2] / 'docker/install/host_security.py')
hs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hs)
_firewall = hs.firewall
hs.firewall = lambda root, live: _firewall(root, False)  # never run iptables or nft of the machine running the test


def tcp_row(address_hex, port, state='0A'):
    return f'  0: {address_hex}:{port:04X} 00000000:0000 {state} 00000000:00000000 00:00000000 00000000     0        0 1 1 0 0 0 0 0'


class Host:
    def __init__(self, sshd='', keys=True, ports=(), ufw=False, ufw_open=None):
        self.dir = tempfile.TemporaryDirectory()
        root = Path(self.dir.name)
        (root / 'etc/ssh/sshd_config.d').mkdir(parents=True)
        (root / 'etc/ssh/sshd_config').write_text(sshd)
        (root / 'etc/passwd').write_text('root:x:0:0::/root:/bin/bash\nsvc:x:5:5::/nonexistent:/usr/sbin/nologin\n')
        (root / 'root/.ssh').mkdir(parents=True)
        if keys:
            (root / 'root/.ssh/authorized_keys').write_text('ssh-ed25519 AAAA test\n')
        if ufw:
            (root / 'etc/ufw').mkdir()
            (root / 'etc/ufw/ufw.conf').write_text('ENABLED=yes\n')
            if ufw_open is not None:
                (root / 'etc/ufw/user.rules').write_text(''.join(f'-A ufw-user-input -p tcp --dport {p} -j ACCEPT\n' for p in ufw_open))
        (root / 'proc/net').mkdir(parents=True)
        (root / 'proc/net/tcp').write_text('header\n' + '\n'.join(tcp_row(a, p) for a, p in ports) + '\n')
        self.root = str(root)

    def ids(self, live=True, **kw):
        return {f['id']: f for f in hs.check(self.root, os.path.join(self.root, 'proc'), live=live, **kw)['findings']}


class Tests(unittest.TestCase):
    def test_passwords_and_open_port_and_no_firewall_are_found(self):
        host = Host('PasswordAuthentication yes\nPermitRootLogin yes\n', ports=[('00000000', 5432), ('00000000', 22), ('0100007F', 6000)])
        found = host.ids()
        self.assertEqual(set(found), {'ssh-password', 'ssh-root-password', 'port-tcp-5432', 'no-firewall'})
        self.assertTrue(found['ssh-password']['fixable'])

    def test_default_sshd_allows_passwords_but_root_needs_a_key(self):
        self.assertEqual(set(Host('').ids()) - {'no-firewall'}, {'ssh-password'})

    def test_clean_server_has_no_findings(self):
        host = Host('PasswordAuthentication no\n', ports=[('00000000', 80), ('00000000', 443), ('00000000', 5671)], ufw=True)
        self.assertEqual(host.ids(), {})

    def test_first_value_wins_across_includes(self):
        host = Host('Include /etc/ssh/sshd_config.d/*.conf\nPasswordAuthentication yes\n', ufw=True)
        Path(host.root, 'etc/ssh/sshd_config.d/50.conf').write_text('PasswordAuthentication no\n')
        self.assertEqual(host.ids(), {})

    def test_no_authorized_key_means_passwords_are_not_fixable(self):
        found = Host('PasswordAuthentication yes\n', keys=False, ufw=True).ids()
        self.assertFalse(found['ssh-password']['fixable'])

    def test_port_blocked_by_ufw_is_not_reported_but_an_allowed_one_is(self):
        ports = [('00000000', 5432), ('00000000', 8080)]
        found = Host('PasswordAuthentication no\n', ports=ports, ufw=True, ufw_open=[22, 8080]).ids()
        self.assertEqual(set(found), {'port-tcp-8080'})

    def test_docker_published_port_is_reported_even_when_ufw_does_not_allow_it(self):
        host = Host('PasswordAuthentication no\n', ports=[('00000000', 6379)], ufw=True, ufw_open=[22])
        self.assertEqual(set(host.ids(docker_ports=[6379])), {'port-tcp-6379'})

    def test_ufw_rules_it_cannot_reason_about_report_every_port(self):
        host = Host('PasswordAuthentication no\n', ports=[('00000000', 6379)], ufw=True, ufw_open=[22])
        with open(os.path.join(host.root, 'etc/ufw/user.rules'), 'a') as rules:
            rules.write('-A ufw-user-input -s 10.0.0.0/8 -j ACCEPT\n')
        self.assertEqual(set(host.ids()), {'port-tcp-6379'})

    def test_unreadable_sshd_config_is_unknown_not_clean(self):
        host = Host('')
        os.remove(os.path.join(host.root, 'etc/ssh/sshd_config'))
        self.assertFalse(hs.check(host.root, os.path.join(host.root, 'proc'), live=False)['sshConfigRead'])

    def test_read_only_helper_does_not_guess_about_the_firewall(self):
        self.assertNotIn('no-firewall', Host('PasswordAuthentication no\n').ids(live=False))

    def test_extra_allowed_port_and_private_addresses_are_ignored(self):
        host = Host('PasswordAuthentication no\n', ufw=True, ports=[('00000000', 53), ('0100A8C0', 9000)])
        self.assertEqual(host.ids(allowed=[53]), {})


    def test_ask_reads_a_yes_typed_on_a_real_terminal(self):
        import pty, time
        path = Path(__file__).resolve().parents[2] / 'docker/install/host_security.py'
        code = ("import importlib.util,sys;s=importlib.util.spec_from_file_location('h',sys.argv[1]);m=importlib.util.module_from_spec(s);"
                "s.loader.exec_module(m);print('ANSWER',m.ask('Fix it?'),m.ask('Again?'))")
        pid, fd = pty.fork()
        if pid == 0:
            os.execvp('python3', ['python3', '-c', code, str(path)])
        os.write(fd, b'y\nn\n')
        out = b''
        deadline = time.time() + 10
        while b'ANSWER' not in out or not out.rstrip().endswith(b'False') and time.time() < deadline:
            try:
                chunk = os.read(fd, 1024)
            except OSError:
                break
            if not chunk:
                break
            out += chunk
        os.waitpid(pid, 0)
        self.assertIn(b'ANSWER True False', out)


if __name__ == '__main__':
    unittest.main()
