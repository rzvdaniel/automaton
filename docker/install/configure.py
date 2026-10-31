#!/usr/bin/env python3
"""Create installation configuration once; never regenerate existing credentials."""
import argparse
import base64
import getpass
import json
import os
from pathlib import Path
import re
import secrets
import tempfile
from cryptography.hazmat.primitives.asymmetric import rsa


def write_new(path, content):
    # Link a fully written private temporary file into place without replacing an existing file.
    with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(content)
    try:
        try:
            os.link(temporary, path)
        except FileExistsError:
            pass
    finally:
        temporary.unlink()



def replace_json(path, value):
    # Atomically rewrite a configuration file in place, keeping its permissions.
    mode = path.stat().st_mode & 0o777
    with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(json.dumps(value, indent=2))
        stream.flush()
        os.fsync(stream.fileno())
    try:
        temporary.chmod(mode)
        os.replace(temporary, path)
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise


def public_urls(domain):
    return {name: f'https://{name}.{domain}' for name in ('api', 'studio', 'apps', 'store')}


def store_router(domain, resolver):
    return dict(entryPoints=['websecure'], rule=f'Host(`store.{domain}`)', service='store',
                tls={'certResolver': resolver}, priority=100)


def ensure_store(root):
    """Add the public app store to an installation; idempotent and safe for existing installations."""
    state = json.loads((root / 'installation.json').read_text())
    domain = state['domain']
    resolver = 'cloudflare' if state.get('tls_mode') == 'cloudflare' else 'letsencrypt'
    urls = public_urls(domain)
    store_config = root / 'store-runtime-config.js'
    write_new(store_config, 'globalThis.__AUTOMATON_CONFIG__ = ' + json.dumps(dict(
        apiBaseUrl=urls['api'], publicAppsBaseUrl=urls['apps'], studioUrl=urls['studio'], storeTitle='App Store')) + ';\n')
    store_config.chmod(0o644)  # Public URLs only; readable by the nginx worker through its bind mount.
    settings_path = root / 'appsettings.Production.json'
    settings = json.loads(settings_path.read_text())
    origins = settings.setdefault('Cors', {}).setdefault('AllowedOrigins', []) if isinstance(settings, dict) else None
    if not isinstance(origins, list):
        raise ValueError(f'{settings_path} must contain Cors.AllowedOrigins as a list.')
    if urls['store'] not in origins:
        origins.append(urls['store'])
        replace_json(settings_path, settings)
    (root / 'dynamic').mkdir(exist_ok=True, mode=0o700)
    # A separate dynamic file: adopted installations may keep a hand-written YAML installation.yml.
    write_new(root / 'dynamic' / 'store.yml', json.dumps({'http': {
        'routers': {'store': store_router(domain, resolver)},
        'services': {'store': {'loadBalancer': {'servers': [{'url': 'http://store:80'}]}}},
    }}))


def secret(root, name, length=32):
    path = root / 'secrets' / name
    write_new(path, secrets.token_hex(length))
    return path.read_text().strip()


def rsa_settings(root):
    path = root / 'secrets' / 'signing-key.json'
    if not path.exists():
        key = rsa.generate_private_key(public_exponent=65537, key_size=3072).private_numbers()
        values = dict(Modulus=key.public_numbers.n, Exponent=key.public_numbers.e,
                      P=key.p, Q=key.q, DP=key.dmp1, DQ=key.dmq1, InverseQ=key.iqmp, D=key.d)
        def xml(names):
            def encoded(value):
                # RSA XML requires fixed-width components, including leading zero bytes.
                width = 384 if value in ('Modulus', 'D') else 192
                if value == 'Exponent':
                    width = (values[value].bit_length() + 7) // 8
                return base64.b64encode(values[value].to_bytes(width, 'big')).decode()
            return '<RSAKeyValue>' + ''.join(f'<{n}>{encoded(n)}</{n}>' for n in names) + '</RSAKeyValue>'
        write_new(path, json.dumps(dict(RsaPrivateKey=xml(values),
                                       RsaPublicKey=xml(('Modulus', 'Exponent')), IsIssuer=True)))
    return json.loads(path.read_text())


def configure(root, domain, email, tls_mode="http", runner_image="rzvdaniel/automaton-runner:latest"):
    if not re.fullmatch(r'(?=.{1,180}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}', domain):
        raise ValueError('Use a public domain such as example.com, without a scheme or path.')
    if not re.fullmatch(r'[^\s@]+@[^\s@]+\.[^\s@]+', email):
        raise ValueError('A valid certificate notification email is required.')
    if (root / 'appsettings.Production.json').exists() or (root / 'installation.json').exists():
        required = ['mysql-password', 'mysql-root-password', 'rabbitmq-password', 'setup-token',
                    'signing-key.json', 'hmac-key', 'ai-encryption-key', 'runtime-proxy-key', 'cloudflare-token']
        if not (root / 'installation.json').exists() or any(not (root / 'secrets' / name).is_file() for name in required):
            raise ValueError('Existing installation credentials are incomplete. Restore the configuration backup; keys will not be regenerated.')
    root.mkdir(parents=True, exist_ok=True, mode=0o700)
    (root / 'secrets').mkdir(exist_ok=True, mode=0o700)
    (root / 'dynamic').mkdir(exist_ok=True, mode=0o700)
    state = root / 'installation.json'
    desired = dict(domain=domain, email=email, tls_mode=tls_mode)
    if state.exists() and json.loads(state.read_text()) != desired:
        raise ValueError('This directory belongs to another domain/email. Existing configuration was preserved.')
    cloudflare = root / 'secrets' / 'cloudflare-token'
    if tls_mode == "http":
        write_new(cloudflare, "")
    elif not cloudflare.exists():
        token = os.environ.get('AUTOMATON_CLOUDFLARE_TOKEN') or getpass.getpass('Cloudflare DNS API token (Zone Read + DNS Edit): ')
        if not re.fullmatch(r'[A-Za-z0-9_-]{20,200}', token):
            raise ValueError('A Cloudflare DNS token is required for wildcard application certificates.')
        write_new(cloudflare, token)
    password = secret(root, 'mysql-password')
    secret(root, 'mysql-root-password')
    broker_password = secret(root, 'rabbitmq-password')
    setup_token = secret(root, 'setup-token')
    urls = public_urls(domain)
    api, studio, apps, store = urls['api'], urls['studio'], urls['apps'], urls['store']
    resolver = 'cloudflare' if tls_mode == 'cloudflare' else 'letsencrypt'
    settings = {
        'ConnectionStrings': {'MySqlConnection': f'Server=mysql;Database=automaton;User=automaton;Password={password};'},
        'RabbitMq': dict(Host='rabbitmq', Port=5672, Username='automaton', Password=broker_password,
                         VirtualHost='/', ManagementUrl='http://rabbitmq:15672/'),
        'HostedRunnerRabbitMq': dict(Host=f'api.{domain}', Port=5671, TlsEnabled=True, TlsServerName=f'api.{domain}'),
        'InfrastructureRunnerRabbitMq': dict(Host=f'api.{domain}', Port=5671, TlsEnabled=True, TlsServerName=f'api.{domain}'),
        'Bootstrap': {'Token': setup_token},
        'Cors': {'AllowedOrigins': [studio, api, store]},
        'JwtOptions': dict(ValidateIssuer=True, Issuer=api, ValidateAudience=False, Audience='Automaton.Studio'),
        'AuthenticationSettings': {'RsaSettings': rsa_settings(root), 'HMacSettings': {'SecretKey': secret(root, 'hmac-key')}},
        'AppConfig': dict(DatabaseType='MySql', ApiUrl=api, WebsiteUrl=studio, UserSignUp=False,
                          AiApiEncryptionKey=secret(root, 'ai-encryption-key', 16)),
        'UserPasswordConfig': dict(RequiredLength=12, RequireDigit=True, RequireUppercase=True,
                                   RequireLowercase=True, RequireNonAlphanumeric=True),
        'CloudflareTurnstile': {'Enabled': False},
        'HostedRunners': {'Enabled': False, 'ServerUrl': api, 'DockerImage': runner_image},
        'PublishedApps': dict(ApiBaseUrl=api, PublicBaseUrl=apps, RootPath='/app/AppData/published-apps',
                              RuntimeProxyAuthKey=secret(root, 'runtime-proxy-key'), TraefikDynamicConfigRoot='/app/AppData/traefik-dynamic', RuntimeTunnelServiceName='api', CertificateResolver=resolver),
        'DataProtection': {'KeysPath': '/app/data-protection-keys'},
        'Logging': {'File': {'RetainedFileCountLimit': 14, 'FileSizeLimitBytes': 10485760}},
        'EmailConfig': {'EmailHost': '', 'EmailAccount': '', 'EmailPassword': ''},
        'GitHub': {'ClientId': '', 'ClientSecret': '', 'ApiBaseUrl': api, 'FrontendUrl': studio},
        'Stripe': {'SecretKey': '', 'PublishableKey': '', 'WebhookSecret': ''},
    }
    # Mark the configuration only after all persistent credentials exist, so a
    # failed initial prompt can be retried without appearing to be a lost backup.
    write_new(state, json.dumps(desired, indent=2))
    write_new(root / 'appsettings.Production.json', json.dumps(settings, indent=2))
    public_config = root / 'studio-runtime-config.js'
    write_new(public_config, 'globalThis.__AUTOMATON_CONFIG__ = ' + json.dumps(dict(apiBaseUrl=api, publicAppsBaseUrl=apps)) + ';\n')
    public_config.chmod(0o644)  # Public URLs only; readable by the nginx worker through its bind mount.
    write_new(root / 'rabbitmq.conf', f'default_user = automaton\ndefault_pass = {broker_password}\ndefault_user_tags.administrator = true\n')
    traefik = {
        'entryPoints': {'web': {'address': ':80', 'http': {'redirections': {'entryPoint': {'to': 'websecure', 'scheme': 'https'}}}},
                        'websecure': {'address': ':443'}, 'broker': {'address': ':5671'}},
        'ping': {}, 'providers': {'file': {'directory': '/etc/traefik/dynamic', 'watch': True}},
        'certificatesResolvers': {'cloudflare': {'acme': {'email': email, 'storage': '/certificates/acme.json',
                                                        'dnsChallenge': {'provider': 'cloudflare'}}}},
        'api': {'dashboard': False},
    }
    if tls_mode == 'http':
        traefik['certificatesResolvers'] = {'letsencrypt': {'acme': {'email': email, 'storage': '/certificates/acme.json', 'httpChallenge': {'entryPoint': 'web'}}}}
    write_new(root / 'traefik.yml', json.dumps(traefik))
    edge = {'http': {'routers': {}, 'services': {
        'api': {'loadBalancer': {'servers': [{'url': 'http://api:8080'}]}},
        'studio': {'loadBalancer': {'servers': [{'url': 'http://studio:80'}]}},
    }}, 'tcp': {'routers': {'broker': {'entryPoints': ['broker'], 'rule': f'HostSNI(`api.{domain}`)',
                                     'service': 'broker', 'tls': {'certResolver': resolver}}},
                'services': {'broker': {'loadBalancer': {'servers': [{'address': 'rabbitmq:5672'}]}}}},
            'tls': {'stores': {'default': {'defaultGeneratedCert': {'resolver': 'cloudflare',
                       'domain': {'main': f'apps.{domain}', 'sans': [f'*.apps.{domain}']}}}}}}
    for name, rule, service in [('api', f'Host(`api.{domain}`)', 'api'),
                                ('studio', f'Host(`studio.{domain}`)', 'studio'),
                                ('studio-api', f'Host(`studio.{domain}`) && (PathPrefix(`/api`) || Path(`/auth/github/login`) || PathPrefix(`/signin-github`))', 'api'),
                                ('apps', f'Host(`apps.{domain}`)', 'api')]:
        edge['http']['routers'][name] = dict(entryPoints=['websecure'], rule=rule, service=service,
                                             tls={'certResolver': resolver}, priority=200 if name == 'studio-api' else 1 if name == 'apps' else 100)
    if tls_mode == 'http':
        edge.pop('tls')
    write_new(root / 'dynamic' / 'installation.yml', json.dumps(edge))
    ensure_store(root)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--config-dir', required=True, type=Path)
    parser.add_argument('--domain')
    parser.add_argument('--email')
    parser.add_argument('--ensure-store', action='store_true',
                        help='Add the app store to an existing installation without changing credentials.')
    parser.add_argument('--tls-mode', choices=['http', 'cloudflare'], default='http')
    parser.add_argument('--runner-image', default='rzvdaniel/automaton-runner:latest')
    args = parser.parse_args()
    os.umask(0o077)
    try:
        if args.ensure_store:
            if not (args.config_dir / 'installation.json').is_file():
                raise ValueError('No existing installation configuration was found.')
            ensure_store(args.config_dir)
        elif not args.domain or not args.email:
            parser.error('--domain and --email are required.')
        else:
            configure(args.config_dir, args.domain.lower(), args.email, args.tls_mode, args.runner_image)
    except (ValueError, OSError, KeyError) as error:  # JSONDecodeError is a ValueError.
        parser.exit(1, f'Configuration failed: {error}\n')
