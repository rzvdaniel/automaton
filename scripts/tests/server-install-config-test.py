#!/usr/bin/env python3
"""Exercise configuration preservation and safe fresh-install generation in temporary directories."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

source = Path(__file__).resolve().parents[2] / 'docker/install/configure.py'
spec = importlib.util.spec_from_file_location('configure_installation', source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class InstallationConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / 'configuration'
        self.environment = patch.dict(os.environ, AUTOMATON_CLOUDFLARE_TOKEN='test-cloudflare-token-not-a-secret')
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def configure(self):
        module.configure(self.root, 'example.com', 'owner@example.com')

    def test_mysql_installation_is_refused_unchanged(self):
        self.configure()
        (self.root / 'secrets/mysql-password').write_text('legacy')
        (self.root / 'secrets/postgres-password').unlink()
        with self.assertRaisesRegex(ValueError, 'runs on MySQL'):
            self.configure()
        self.assertFalse((self.root / 'secrets/postgres-password').exists())

    def test_rerun_preserves_all_secrets_and_modified_operator_settings(self):
        self.configure()
        path = self.root / 'appsettings.Production.json'
        config = json.loads(path.read_text())
        config['EmailConfig']['EmailHost'] = 'mail.example.com'
        config['Catalog']['InstallEssentialsOnFirstRunner'] = False
        path.write_text(json.dumps(config))
        before = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*') if p.is_file()}
        self.configure()
        after = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*') if p.is_file()}
        self.assertEqual(before, after)

    def test_fresh_install_enables_essential_catalog_apps(self):
        self.configure()
        settings = json.loads((self.root / 'appsettings.Production.json').read_text())
        self.assertTrue(settings['Catalog']['InstallEssentialsOnFirstRunner'])

    def test_generated_credentials_are_private_unique_and_connected(self):
        self.configure()
        settings = json.loads((self.root / 'appsettings.Production.json').read_text())
        password = (self.root / 'secrets/postgres-password').read_text()
        self.assertIn(password, settings['ConnectionStrings']['PostgreSqlConnection'])
        self.assertEqual(len(settings['AppConfig']['AiApiEncryptionKey']), 32)
        self.assertFalse(settings['AppConfig']['UserSignUp'])
        self.assertEqual(settings['AppConfig']['ApiUrl'], 'https://api.example.com')
        self.assertEqual(settings['PublishedApps']['CertificateResolver'], 'letsencrypt')
        self.assertEqual((self.root / 'secrets/cloudflare-token').read_text(), '')
        self.assertEqual(settings['PublishedApps']['RuntimeTunnelServiceName'], 'api')
        edge = json.loads((self.root / 'dynamic/installation.yml').read_text())
        self.assertGreater(edge['http']['routers']['studio-api']['priority'], edge['http']['routers']['studio']['priority'])
        self.assertIn('Path(`/auth/github/login`)', edge['http']['routers']['studio-api']['rule'])
        self.assertNotIn('PathPrefix(`/auth/github`)', edge['http']['routers']['studio-api']['rule'])
        # Studio pages such as /apikeys must not match the API route.
        self.assertIn('PathPrefix(`/api/`)', edge['http']['routers']['studio-api']['rule'])
        self.assertNotIn('PathPrefix(`/api`)', edge['http']['routers']['studio-api']['rule'])
        pages = json.loads((self.root / 'dynamic/studio-paths.yml').read_text())['http']
        self.assertEqual(pages['routers']['studio-pages']['rule'], r'HostRegexp(`^studio\.`) && PathRegexp(`^/api[^/]`)')
        self.assertGreater(pages['routers']['studio-pages']['priority'], edge['http']['routers']['studio-api']['priority'])
        self.assertEqual(pages['services']['studio-pages']['loadBalancer']['servers'], [{'url': 'http://studio:80'}])
        self.assertNotEqual(password, (self.root / 'secrets/rabbitmq-password').read_text())
        self.assertIn('https://api.example.com', (self.root / 'studio-runtime-config.js').read_text())
        self.assertIn('https://apps.example.com', (self.root / 'studio-runtime-config.js').read_text())
        for path in self.root.rglob('*'):
            if path.is_file(): self.assertEqual(path.stat().st_mode & 0o777, 0o644 if path.name.endswith('-runtime-config.js') else 0o600)
        from cryptography.hazmat.primitives.asymmetric import rsa
        import base64
        import xml.etree.ElementTree as xml
        values = xml.fromstring(settings['AuthenticationSettings']['RsaSettings']['RsaPrivateKey'])
        numbers = {node.tag: int.from_bytes(base64.b64decode(node.text), 'big') for node in values}
        key = rsa.RSAPrivateNumbers(numbers['P'], numbers['Q'], numbers['D'], numbers['DP'], numbers['DQ'], numbers['InverseQ'],
                                    rsa.RSAPublicNumbers(numbers['Exponent'], numbers['Modulus'])).private_key()
        self.assertEqual(key.key_size, 3072)

    def test_fresh_installation_includes_app_store(self):
        self.configure()
        settings = json.loads((self.root / 'appsettings.Production.json').read_text())
        self.assertIn('https://store.example.com', settings['Cors']['AllowedOrigins'])
        edge = json.loads((self.root / 'dynamic/store.yml').read_text())
        self.assertEqual(edge['http']['routers']['store']['rule'], 'Host(`store.example.com`)')
        self.assertNotIn('store', json.loads((self.root / 'dynamic/installation.yml').read_text())['http']['routers'])
        self.assertEqual(edge['http']['routers']['store']['service'], 'store')
        self.assertEqual(edge['http']['services']['store']['loadBalancer']['servers'], [{'url': 'http://store:80'}])
        store_config = (self.root / 'store-runtime-config.js').read_text()
        for url in ('https://api.example.com', 'https://apps.example.com', 'https://studio.example.com'):
            self.assertIn(url, store_config)

    def test_ensure_store_migrates_existing_installation_once(self):
        module.configure(self.root, 'example.com', 'owner@example.com', 'cloudflare')
        # Recreate a configuration written before the app store existed, with an operator edit.
        settings_path = self.root / 'appsettings.Production.json'
        settings = json.loads(settings_path.read_text())
        settings['Cors']['AllowedOrigins'].remove('https://store.example.com')
        settings['EmailConfig']['EmailHost'] = 'mail.example.com'
        settings_path.write_text(json.dumps(settings))
        # Adopted installations may keep a hand-written YAML installation.yml; it must stay untouched.
        installation_path = self.root / 'dynamic/installation.yml'
        installation_path.write_text('http:\n  routers: {}\n')
        (self.root / 'dynamic/store.yml').unlink()
        (self.root / 'store-runtime-config.js').unlink()
        secrets_before = {p.name: p.read_bytes() for p in (self.root / 'secrets').iterdir()}

        module.ensure_store(self.root)

        settings = json.loads(settings_path.read_text())
        self.assertEqual(settings['Cors']['AllowedOrigins'].count('https://store.example.com'), 1)
        self.assertEqual(settings['EmailConfig']['EmailHost'], 'mail.example.com')
        self.assertEqual(settings_path.stat().st_mode & 0o777, 0o600)
        edge = json.loads((self.root / 'dynamic/store.yml').read_text())
        self.assertEqual(edge['http']['routers']['store']['tls'], {'certResolver': 'cloudflare'})
        self.assertEqual(installation_path.read_text(), 'http:\n  routers: {}\n')
        self.assertEqual((self.root / 'store-runtime-config.js').stat().st_mode & 0o777, 0o644)
        self.assertEqual(secrets_before, {p.name: p.read_bytes() for p in (self.root / 'secrets').iterdir()})
        before = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*') if p.is_file()}
        module.ensure_store(self.root)
        self.assertEqual(before, {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*') if p.is_file()})

    def test_ensure_studio_paths_migrates_existing_installation_once(self):
        module.configure(self.root, 'example.com', 'owner@example.com', 'cloudflare')
        # An installation from before this router, with a hand-written YAML installation.yml that must stay untouched.
        installation_path = self.root / 'dynamic/installation.yml'
        installation_path.write_text('http:\n  routers: {}\n')
        (self.root / 'dynamic/studio-paths.yml').write_text('{"http": {"routers": {"studio-pages": {"rule": "Host(`studio.example.com`) && PathRegexp(`^/api[^/]`)"}}}}')

        module.ensure_studio_paths(self.root)

        pages = json.loads((self.root / 'dynamic/studio-paths.yml').read_text())['http']['routers']['studio-pages']
        self.assertEqual(pages['tls'], {'certResolver': 'cloudflare'})
        self.assertEqual(installation_path.read_text(), 'http:\n  routers: {}\n')
        before = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*') if p.is_file()}
        module.ensure_studio_paths(self.root)
        self.assertEqual(before, {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*') if p.is_file()})

    def test_domain_change_does_not_rewrite_existing_installation(self):
        self.configure()
        original = (self.root / 'appsettings.Production.json').read_bytes()
        with self.assertRaises(ValueError): module.configure(self.root, 'another.example.com', 'owner@example.com')
        self.assertEqual(original, (self.root / 'appsettings.Production.json').read_bytes())

    def test_missing_existing_key_is_not_silently_regenerated(self):
        self.configure()
        key = self.root / 'secrets/signing-key.json'
        key.unlink()
        with self.assertRaises(ValueError): self.configure()
        self.assertFalse(key.exists())

    def test_missing_settings_and_key_are_not_silently_regenerated(self):
        self.configure()
        settings = self.root / 'appsettings.Production.json'
        key = self.root / 'secrets/signing-key.json'
        settings.unlink()
        key.unlink()
        with self.assertRaises(ValueError): self.configure()
        self.assertFalse(settings.exists())
        self.assertFalse(key.exists())

    def test_failed_initial_token_prompt_can_be_retried(self):
        with patch.dict(os.environ, AUTOMATON_CLOUDFLARE_TOKEN='invalid'):
            with self.assertRaises(ValueError):
                module.configure(self.root, 'example.com', 'owner@example.com', 'cloudflare')
        self.assertFalse((self.root / 'installation.json').exists())
        module.configure(self.root, 'example.com', 'owner@example.com', 'cloudflare')
        self.assertTrue((self.root / 'appsettings.Production.json').exists())

    def test_cloudflare_mode_generates_wildcard_certificate_configuration(self):
        module.configure(self.root, 'example.com', 'owner@example.com', 'cloudflare')
        edge = json.loads((self.root / 'dynamic/installation.yml').read_text())
        self.assertEqual(edge['http']['routers']['apps']['rule'], 'Host(`apps.example.com`)')
        self.assertNotIn('HostRegexp', json.dumps(edge['http']['routers']))
        edge = json.loads((self.root / 'dynamic/installation.yml').read_text())
        self.assertIn('*.apps.example.com', edge['tls']['stores']['default']['defaultGeneratedCert']['domain']['sans'])
        self.assertTrue((self.root / 'secrets/cloudflare-token').read_text())

    def snapshot(self, *skip):
        return {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*')
                if p.is_file() and p.relative_to(self.root).as_posix() not in skip}

    def test_ensure_custom_domain_resolver_migrates_cloudflare_installation_once(self):
        module.configure(self.root, 'example.com', 'owner@example.com', 'cloudflare')
        traefik_path = self.root / 'traefik.yml'
        traefik = json.loads(traefik_path.read_text())
        del traefik['certificatesResolvers']['letsencrypt']
        traefik_path.write_text(json.dumps(traefik))
        settings_path = self.root / 'appsettings.Production.json'
        settings = json.loads(settings_path.read_text())
        del settings['PublishedApps']['CustomDomainCertificateResolver']
        settings_path.write_text(json.dumps(settings))
        self.assertEqual(module.ensure_custom_domain_resolver(self.root), 'changed')
        fresh = Path(self.temporary.name) / 'fresh'
        module.configure(fresh, 'example.com', 'owner@example.com', 'cloudflare')
        self.assertEqual(json.loads(traefik_path.read_text()), json.loads((fresh / 'traefik.yml').read_text()))
        self.assertEqual(json.loads(settings_path.read_text())['PublishedApps']['CustomDomainCertificateResolver'], 'letsencrypt')
        before = self.snapshot()
        self.assertEqual(module.ensure_custom_domain_resolver(self.root), 'unchanged')
        self.assertEqual(before, self.snapshot())

    def test_access_log_is_json_without_headers_and_migrates_existing_installation_once(self):
        module.configure(self.root, 'example.com', 'owner@example.com')
        traefik_path = self.root / 'traefik.yml'
        access_log = json.loads(traefik_path.read_text())['accessLog']
        self.assertEqual(access_log['format'], 'json')
        self.assertEqual(access_log['filePath'], '/certificates/logs/access.log')
        self.assertEqual(access_log['fields']['headers'], {'defaultMode': 'drop', 'names': {'X-Automaton-Auth-Failure': 'keep'}})
        before = json.loads(traefik_path.read_text())
        del before['accessLog']
        traefik_path.write_text(json.dumps(before))
        self.assertEqual(module.ensure_access_log(self.root), 'changed')
        self.assertEqual(json.loads(traefik_path.read_text())['accessLog'], access_log)
        self.assertEqual(module.ensure_access_log(self.root), 'unchanged')
        before['accessLog'] = module.PREVIOUS_ACCESS_LOG
        traefik_path.write_text(json.dumps(before))
        self.assertEqual(module.ensure_access_log(self.root), 'changed')
        self.assertEqual(json.loads(traefik_path.read_text())['accessLog'], access_log)
        before['accessLog'] = {'filePath': '/elsewhere.log', 'format': 'common'}
        traefik_path.write_text(json.dumps(before))
        self.assertEqual(module.ensure_access_log(self.root), 'unchanged')
        traefik_path.write_text('entryPoints: {}  # hand written\n')
        self.assertEqual(module.ensure_access_log(self.root), 'skipped')

    def test_ensure_custom_domain_resolver_skips_hand_written_traefik_and_http_mode(self):
        module.configure(self.root, 'example.com', 'owner@example.com', 'cloudflare')
        (self.root / 'traefik.yml').write_text('entryPoints: {}\n')
        before = self.snapshot()
        self.assertEqual(module.ensure_custom_domain_resolver(self.root), 'skipped')
        self.assertEqual(before, self.snapshot())
        other = Path(self.temporary.name) / 'http'
        module.configure(other, 'example.com', 'owner@example.com')
        self.assertEqual(module.ensure_custom_domain_resolver(other), 'unchanged')

    def test_ensure_runner_broker_host_replaces_private_address_only(self):
        module.configure(self.root, 'example.com', 'owner@example.com')
        path = self.root / 'appsettings.Production.json'
        settings = json.loads(path.read_text())
        local = dict(settings['InfrastructureRunnerRabbitMq'])
        settings['HostedRunnerRabbitMq'] = dict(Host='10.0.0.3', Port=5672, TlsEnabled=False, TlsServerName=None)
        path.write_text(json.dumps(settings))
        self.assertEqual(module.ensure_runner_broker_host(self.root), 'changed')
        migrated = json.loads(path.read_text())
        self.assertEqual(migrated['HostedRunnerRabbitMq'], dict(Host='api.example.com', Port=5671, TlsEnabled=True, TlsServerName='api.example.com'))
        self.assertEqual(migrated['InfrastructureRunnerRabbitMq'], local)
        before = self.snapshot()
        self.assertEqual(module.ensure_runner_broker_host(self.root), 'unchanged')
        self.assertEqual(before, self.snapshot())

    def test_ensure_runner_broker_host_keeps_custom_public_host_and_fresh_install(self):
        module.configure(self.root, 'example.com', 'owner@example.com')
        self.assertEqual(module.ensure_runner_broker_host(self.root), 'unchanged')
        path = self.root / 'appsettings.Production.json'
        settings = json.loads(path.read_text())
        settings['HostedRunnerRabbitMq']['Host'] = 'broker.other.net'
        path.write_text(json.dumps(settings))
        self.assertEqual(module.ensure_runner_broker_host(self.root), 'unchanged')
        settings['HostedRunnerRabbitMq']['Host'] = 'rabbitmq'
        path.write_text(json.dumps(settings))
        self.assertEqual(module.ensure_runner_broker_host(self.root), 'changed')

    def test_switch_to_cloudflare_rewrites_only_tls_files(self):
        self.configure()
        touched = ('traefik.yml', 'dynamic/installation.yml', 'dynamic/store.yml', 'dynamic/studio-paths.yml',
                   'appsettings.Production.json', 'installation.json', 'secrets/cloudflare-token')
        before = self.snapshot(*touched)
        self.assertTrue(module.switch_tls(self.root, 'cloudflare'))
        self.assertEqual(before, self.snapshot(*touched))  # credentials, keys and other configuration untouched
        fresh = Path(self.temporary.name) / 'fresh'
        module.configure(fresh, 'example.com', 'owner@example.com', 'cloudflare')
        for name in ('traefik.yml', 'dynamic/installation.yml', 'dynamic/store.yml', 'dynamic/studio-paths.yml'):
            self.assertEqual(json.loads((fresh / name).read_text()), json.loads((self.root / name).read_text()), name)
        settings = json.loads((self.root / 'appsettings.Production.json').read_text())
        self.assertEqual(settings['PublishedApps']['CertificateResolver'], 'cloudflare')
        self.assertEqual(json.loads((self.root / 'installation.json').read_text())['tls_mode'], 'cloudflare')
        self.assertEqual((self.root / 'secrets/cloudflare-token').read_text(), 'test-cloudflare-token-not-a-secret')
        self.assertFalse(module.switch_tls(self.root, 'cloudflare'))  # idempotent; keeps the token

    def make_certificate(self, names, name='cert'):
        folder = Path(self.temporary.name)
        cert, key = folder / f'{name}.pem', folder / f'{name}.key'
        subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '30', '-subj', '/CN=test',
                        '-addext', 'subjectAltName=' + ','.join('DNS:' + n for n in names),
                        '-keyout', str(key), '-out', str(cert)], check=True, capture_output=True)
        return cert, key

    def test_custom_mode_serves_the_given_certificate_without_acme(self):
        cert, key = self.make_certificate(['*.example.com', '*.apps.example.com'])
        module.configure(self.root, 'example.com', 'owner@example.com', 'custom', cert_file=cert, key_file=key)
        traefik = json.loads((self.root / 'traefik.yml').read_text())
        self.assertNotIn('certificatesResolvers', traefik)
        edge = json.loads((self.root / 'dynamic/installation.yml').read_text())
        for name in ('api', 'studio', 'apps'):
            self.assertEqual(edge['http']['routers'][name]['tls'], {})
        self.assertNotIn('tls', edge)
        self.assertEqual(edge['tcp']['routers']['broker']['tls'], {})
        self.assertEqual(json.loads((self.root / 'dynamic/store.yml').read_text())['http']['routers']['store']['tls'], {})
        settings = json.loads((self.root / 'appsettings.Production.json').read_text())
        self.assertEqual(settings['PublishedApps']['CertificateResolver'], '')
        self.assertEqual(settings['PublishedApps']['CustomDomainCertificateResolver'], '')
        files = json.loads((self.root / 'dynamic/custom-tls.yml').read_text())['tls']['certificates']
        self.assertEqual(files[0]['certFile'], '/etc/traefik/certs/fullchain.pem')
        self.assertEqual((self.root / 'certs/privkey.pem').stat().st_mode & 0o777, 0o600)

    def test_custom_mode_names_every_uncovered_name(self):
        cert, key = self.make_certificate(['*.example.com'])
        with self.assertRaisesRegex(ValueError, r'does not cover: \*\.apps\.example\.com'):
            module.configure(self.root, 'example.com', 'owner@example.com', 'custom', cert_file=cert, key_file=key)
        self.assertFalse((self.root / 'installation.json').exists())

    def test_custom_mode_rejects_a_key_from_another_certificate(self):
        cert, _ = self.make_certificate(['*.example.com', '*.apps.example.com'])
        _, other = self.make_certificate(['*.example.com'], 'other')
        with self.assertRaisesRegex(ValueError, 'does not belong'):
            module.configure(self.root, 'example.com', 'owner@example.com', 'custom', cert_file=cert, key_file=other)

    def test_custom_mode_rerun_for_another_domain_keeps_the_installed_certificate(self):
        cert, key = self.make_certificate(['*.example.com', '*.apps.example.com'])
        module.configure(self.root, 'example.com', 'owner@example.com', 'custom', cert_file=cert, key_file=key)
        before = (self.root / 'certs/fullchain.pem').read_text()
        other, other_key = self.make_certificate(['*.example.org', '*.apps.example.org'], 'org')
        with self.assertRaisesRegex(ValueError, 'belongs to another domain'):
            module.configure(self.root, 'example.org', 'owner@example.com', 'custom', cert_file=other, key_file=other_key)
        self.assertEqual((self.root / 'certs/fullchain.pem').read_text(), before)

    def test_custom_mode_rejects_a_passphrase_key_without_prompting(self):
        cert, key = self.make_certificate(['*.example.com', '*.apps.example.com'])
        locked = key.with_name('locked.key')
        subprocess.run(['openssl', 'pkey', '-in', str(key), '-out', str(locked), '-aes256', '-passout', 'pass:secret'],
                       check=True, capture_output=True)
        with self.assertRaisesRegex(ValueError, 'passphrase'):
            module.configure(self.root, 'example.com', 'owner@example.com', 'custom', cert_file=cert, key_file=locked)

    def test_replace_certificate_swaps_files(self):
        cert, key = self.make_certificate(['*.example.com', '*.apps.example.com'])
        module.configure(self.root, 'example.com', 'owner@example.com', 'custom', cert_file=cert, key_file=key)
        new_cert, new_key = self.make_certificate(['*.example.com', '*.apps.example.com'], 'new')
        module.install_custom_certificate(self.root, 'example.com', new_cert, new_key)
        self.assertEqual((self.root / 'certs/fullchain.pem').read_text(), new_cert.read_text())

    def test_delegated_mode_uses_httpreq_challenge_and_no_token(self):
        module.configure(self.root, 'example.com', 'owner@example.com', 'delegated')
        traefik = json.loads((self.root / 'traefik.yml').read_text())
        challenge = traefik['certificatesResolvers']['delegated']['acme']['dnsChallenge']
        self.assertEqual(challenge['provider'], 'httpreq')
        edge = json.loads((self.root / 'dynamic/installation.yml').read_text())
        routers = edge['http']['routers']
        self.assertEqual(routers['apps']['tls']['certResolver'], 'delegated')
        for name in ('api', 'studio', 'studio-api'):
            self.assertEqual(routers[name]['tls']['certResolver'], 'letsencrypt', name)
        self.assertEqual(edge['tcp']['routers']['broker']['tls']['certResolver'], 'letsencrypt')
        for name, router in (('store.yml', 'store'), ('studio-paths.yml', 'studio-pages')):
            extra = json.loads((self.root / 'dynamic' / name).read_text())
            self.assertEqual(extra['http']['routers'][router]['tls']['certResolver'], 'letsencrypt', name)
        self.assertEqual(edge['tls']['stores']['default']['defaultGeneratedCert']['resolver'], 'delegated')
        self.assertEqual((self.root / 'secrets/cloudflare-token').read_text(), '')

    def test_switch_to_delegated_matches_fresh_and_back_to_cloudflare_and_http(self):
        self.configure()
        self.assertTrue(module.switch_tls(self.root, 'delegated'))
        fresh = Path(self.temporary.name) / 'fresh'
        module.configure(fresh, 'example.com', 'owner@example.com', 'delegated')
        for name in ('traefik.yml', 'dynamic/installation.yml', 'dynamic/store.yml', 'dynamic/studio-paths.yml'):
            self.assertEqual(json.loads((fresh / name).read_text()), json.loads((self.root / name).read_text()), name)
        settings = json.loads((self.root / 'appsettings.Production.json').read_text())
        self.assertEqual(settings['PublishedApps']['CertificateResolver'], 'delegated')
        self.assertEqual(json.loads((self.root / 'dynamic/installation.yml').read_text())['http']['routers']['studio']['tls']['certResolver'], 'letsencrypt')
        self.assertFalse(module.switch_tls(self.root, 'delegated'))
        # A server installed before #1583 had DNS-01 on every host; switching delegated to delegated repairs it.
        edge_path = self.root / 'dynamic/installation.yml'
        edge = json.loads(edge_path.read_text())
        edge['http']['routers']['studio']['tls']['certResolver'] = 'delegated'
        edge_path.write_text(json.dumps(edge))
        self.assertTrue(module.switch_tls(self.root, 'delegated'))
        self.assertEqual(json.loads(edge_path.read_text())['http']['routers']['studio']['tls']['certResolver'], 'letsencrypt')
        self.assertFalse(module.switch_tls(self.root, 'delegated'))
        self.assertTrue(module.switch_tls(self.root, 'http'))
        self.assertNotIn('tls', json.loads((self.root / 'dynamic/installation.yml').read_text()))

    def test_switch_back_to_http_matches_fresh_http_and_empties_token(self):
        module.configure(self.root, 'example.com', 'owner@example.com', 'cloudflare')
        self.assertTrue(module.switch_tls(self.root, 'http'))
        fresh = Path(self.temporary.name) / 'fresh'
        module.configure(fresh, 'example.com', 'owner@example.com')
        for name in ('traefik.yml', 'dynamic/installation.yml', 'dynamic/store.yml', 'dynamic/studio-paths.yml'):
            self.assertEqual(json.loads((fresh / name).read_text()), json.loads((self.root / name).read_text()), name)
        self.assertEqual((self.root / 'secrets/cloudflare-token').read_text(), '')
        self.assertEqual(json.loads((self.root / 'installation.json').read_text())['tls_mode'], 'http')

    def test_switch_reads_token_from_file_and_rejects_bad_token_unchanged(self):
        self.configure()
        before = self.snapshot()
        bad = Path(self.temporary.name) / 'bad-token'
        bad.write_text('short')
        with self.assertRaises(ValueError): module.switch_tls(self.root, 'cloudflare', bad)
        self.assertEqual(before, self.snapshot())
        good = Path(self.temporary.name) / 'token'
        good.write_text('file-cloudflare-token-0123456789\n')
        with patch.dict(os.environ, AUTOMATON_CLOUDFLARE_TOKEN='environment-token-should-not-win-0'):
            module.switch_tls(self.root, 'cloudflare', good)
        self.assertEqual((self.root / 'secrets/cloudflare-token').read_text(), 'file-cloudflare-token-0123456789')

    def test_switch_refuses_hand_written_traefik_configuration_unchanged(self):
        self.configure()
        (self.root / 'dynamic/installation.yml').write_text('http:\n  routers: {}\n')
        before = self.snapshot()
        with self.assertRaises(ValueError): module.switch_tls(self.root, 'cloudflare')
        self.assertEqual(before, self.snapshot())

    def test_switch_does_not_change_domain_or_email(self):
        self.configure()
        result = subprocess.run([sys.executable, str(source), '--config-dir', str(self.root), '--switch-tls',
                                 '--tls-mode', 'cloudflare', '--domain', 'other.example.com'], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(json.loads((self.root / 'installation.json').read_text())['domain'], 'example.com')
        self.assertNotIn('test-cloudflare-token-not-a-secret', result.stdout + result.stderr)

    def test_invalid_domain_fails_before_creating_files(self):
        with self.assertRaises(ValueError): module.configure(self.root, 'https://example.com;command', 'owner@example.com')
        self.assertFalse(self.root.exists())


if __name__ == '__main__': unittest.main()
