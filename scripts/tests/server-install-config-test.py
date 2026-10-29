#!/usr/bin/env python3
"""Exercise configuration preservation and safe fresh-install generation in temporary directories."""
import importlib.util
import json
import os
from pathlib import Path
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

    def test_rerun_preserves_all_secrets_and_modified_operator_settings(self):
        self.configure()
        path = self.root / 'appsettings.Production.json'
        config = json.loads(path.read_text())
        config['EmailConfig']['EmailHost'] = 'mail.example.com'
        path.write_text(json.dumps(config))
        before = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*') if p.is_file()}
        self.configure()
        after = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob('*') if p.is_file()}
        self.assertEqual(before, after)

    def test_generated_credentials_are_private_unique_and_connected(self):
        self.configure()
        settings = json.loads((self.root / 'appsettings.Production.json').read_text())
        password = (self.root / 'secrets/mysql-password').read_text()
        self.assertIn(password, settings['ConnectionStrings']['MySqlConnection'])
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
        self.assertNotEqual(password, (self.root / 'secrets/mysql-root-password').read_text())
        self.assertIn('https://api.example.com', (self.root / 'studio-runtime-config.js').read_text())
        self.assertIn('https://apps.example.com', (self.root / 'studio-runtime-config.js').read_text())
        for path in self.root.rglob('*'):
            if path.is_file(): self.assertEqual(path.stat().st_mode & 0o777, 0o644 if path.name == 'studio-runtime-config.js' else 0o600)
        from cryptography.hazmat.primitives.asymmetric import rsa
        import base64
        import xml.etree.ElementTree as xml
        values = xml.fromstring(settings['AuthenticationSettings']['RsaSettings']['RsaPrivateKey'])
        numbers = {node.tag: int.from_bytes(base64.b64decode(node.text), 'big') for node in values}
        key = rsa.RSAPrivateNumbers(numbers['P'], numbers['Q'], numbers['D'], numbers['DP'], numbers['DQ'], numbers['InverseQ'],
                                    rsa.RSAPublicNumbers(numbers['Exponent'], numbers['Modulus'])).private_key()
        self.assertEqual(key.key_size, 3072)

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

    def test_invalid_domain_fails_before_creating_files(self):
        with self.assertRaises(ValueError): module.configure(self.root, 'https://example.com;command', 'owner@example.com')
        self.assertFalse(self.root.exists())


if __name__ == '__main__': unittest.main()
