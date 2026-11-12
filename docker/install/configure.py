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


def resolver_for(tls_mode):
    return {'cloudflare': 'cloudflare', 'delegated': 'delegated'}.get(tls_mode, 'letsencrypt')


def challenge_zone(domain):
    return f'_acme-challenge.apps.{domain}'


def certificates_resolvers(tls_mode, email):
    """Traefik certificate resolver for a TLS mode. delegated: Let's Encrypt asks our own DNS responder (#891)."""
    acme = {'email': email, 'storage': '/certificates/acme.json'}
    if tls_mode == 'http':
        acme['httpChallenge'] = {'entryPoint': 'web'}
    elif tls_mode == 'delegated':
        # The responder is the authority for the challenge name, so there is nothing to wait for.
        acme['dnsChallenge'] = {'provider': 'httpreq', 'propagation': {'disableChecks': True}}
    else:
        acme['dnsChallenge'] = {'provider': 'cloudflare'}
    return {resolver_for(tls_mode): {'acme': acme}}


def wildcard_store(tls_mode, domain):
    """Default certificate for *.apps.<domain>; http mode has none (one certificate per app)."""
    if tls_mode == 'http':
        return None
    return {'stores': {'default': {'defaultGeneratedCert': {'resolver': resolver_for(tls_mode),
            'domain': {'main': f'apps.{domain}', 'sans': [f'*.apps.{domain}']}}}}}


def public_urls(domain):
    return {name: f'https://{name}.{domain}' for name in ('api', 'studio', 'apps', 'store')}


def store_router(domain, resolver):
    return dict(entryPoints=['websecure'], rule=f'Host(`store.{domain}`)', service='store',
                tls={'certResolver': resolver}, priority=100)


def studio_paths_router(domain, resolver):
    # Studio pages whose path starts with "api" (e.g. /apikeys) belong to Studio, not the API. The studio-api router
    # matches PathPrefix(`/api`) as plain text on installations configured before this router existed.
    # Any studio.* host: a server can also answer on extra hostnames (the test server has studio.test.<domain>).
    return dict(entryPoints=['websecure'], rule=r'HostRegexp(`^studio\.`) && PathRegexp(`^/api[^/]`)',
                service='studio-pages', tls={'certResolver': resolver}, priority=300)


def ensure_studio_paths(root):
    """Route Studio pages such as /apikeys to Studio; idempotent and safe for existing installations."""
    state = json.loads((root / 'installation.json').read_text())
    resolver = resolver_for(state.get('tls_mode'))
    (root / 'dynamic').mkdir(exist_ok=True, mode=0o700)
    # A separate dynamic file with its own service: adopted installations keep a hand-written installation.yml
    # whose Studio service may have another name.
    path = root / 'dynamic' / 'studio-paths.yml'
    content = json.dumps({'http': {
        'routers': {'studio-pages': studio_paths_router(state['domain'], resolver)},
        'services': {'studio-pages': {'loadBalancer': {'servers': [{'url': 'http://studio:80'}]}}},
    }})
    # Unlike the other files, this one is ours and is replaced when its rule changes (earlier versions matched one host).
    if not path.exists() or path.read_text() != content:
        with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, delete=False) as stream:
            stream.write(content)
        os.replace(stream.name, path)


def ensure_store(root):
    """Add the public app store to an installation; idempotent and safe for existing installations."""
    state = json.loads((root / 'installation.json').read_text())
    domain = state['domain']
    resolver = resolver_for(state.get('tls_mode'))
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


def update_runner_image(root, previous, current):
    """Point Studio's Runner install command at a release's Runner image; keep operator-chosen images."""
    path = root / 'appsettings.Production.json'
    settings = json.loads(path.read_text())
    runners = settings.get('HostedRunners') if isinstance(settings, dict) else None
    if not isinstance(runners, dict) or runners.get('DockerImage') not in (previous, current):
        return False
    if runners['DockerImage'] != current:
        runners['DockerImage'] = current
        replace_json(path, settings)
    return True


def read_cloudflare_token(token_file=None):
    """Read the DNS token without echoing it: from a file, AUTOMATON_CLOUDFLARE_TOKEN, or a hidden prompt."""
    if token_file:
        token = Path(token_file).read_text().strip()
    else:
        token = os.environ.get('AUTOMATON_CLOUDFLARE_TOKEN') or getpass.getpass('Cloudflare DNS API token (Zone Read + DNS Edit): ')
    if not re.fullmatch(r'[A-Za-z0-9_-]{20,200}', token):
        raise ValueError('A Cloudflare DNS token is required for wildcard application certificates.')
    return token


def replace_text(path, content, mode=None):
    # Atomically replace a private file, keeping its permissions (0600 for a new one).
    mode = mode or (path.stat().st_mode & 0o777 if path.exists() else 0o600)
    with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(content)
        stream.flush()
        os.fsync(stream.fileno())
    try:
        temporary.chmod(mode)
        os.replace(temporary, path)
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise


def set_resolver(node, resolver):
    """Point every router's certResolver at the resolver, recursively; leave everything else alone."""
    if isinstance(node, dict):
        if isinstance(node.get('tls'), dict) and 'certResolver' in node['tls']:
            node['tls']['certResolver'] = resolver
        for value in node.values():
            set_resolver(value, resolver)
    elif isinstance(node, list):
        for value in node:
            set_resolver(value, resolver)


def switch_tls(root, tls_mode, token_file=None):
    """Switch an existing installation between http and cloudflare certificates. Domain and email stay as they are;
    credentials, the database and volumes are never touched. Returns True when something changed."""
    state_path = root / 'installation.json'
    if not state_path.is_file():
        raise ValueError('No existing installation configuration was found.')
    state = json.loads(state_path.read_text())
    domain, email, current = state['domain'], state['email'], state.get('tls_mode', 'http')
    token_path = root / 'secrets' / 'cloudflare-token'
    have_token = token_path.is_file() and token_path.read_text().strip() != ''
    if tls_mode == current and not (tls_mode == 'cloudflare' and (token_file or not have_token)):
        return False
    resolver = resolver_for(tls_mode)
    token = ''
    if tls_mode == 'cloudflare':
        token = token_path.read_text().strip() if current == 'cloudflare' and have_token and not token_file else read_cloudflare_token(token_file)
    # Prepare every new file in memory first, so a file we cannot rewrite safely stops the switch before any change.
    # Installations adopted from a hand-written setup may use YAML we cannot edit; those are refused untouched.
    traefik_path = root / 'traefik.yml'
    edge_path = root / 'dynamic' / 'installation.yml'
    try:
        traefik = json.loads(traefik_path.read_text())
        edge = json.loads(edge_path.read_text())
    except (OSError, ValueError):
        raise ValueError('traefik.yml or dynamic/installation.yml was not generated by the installer (adopted or hand-edited). '
                         'Nothing was changed; edit the certificate resolver by hand instead.')
    traefik['certificatesResolvers'] = certificates_resolvers(tls_mode, email)
    store = wildcard_store(tls_mode, domain)
    if store:
        edge['tls'] = store
    else:
        edge.pop('tls', None)
    set_resolver(edge, resolver)
    updates = {traefik_path: json.dumps(traefik), edge_path: json.dumps(edge)}
    for name in ('store.yml', 'studio-paths.yml'):
        path = root / 'dynamic' / name
        if path.is_file():
            try:
                extra = json.loads(path.read_text())
            except ValueError:
                raise ValueError(f'dynamic/{name} was not generated by the installer. Nothing was changed.')
            set_resolver(extra, resolver)
            updates[path] = json.dumps(extra)
    settings_path = root / 'appsettings.Production.json'
    settings = json.loads(settings_path.read_text())
    settings.setdefault('PublishedApps', {})['CertificateResolver'] = resolver
    updates[settings_path] = json.dumps(settings, indent=2)
    updates[token_path] = token
    updates[state_path] = json.dumps(dict(state, tls_mode=tls_mode), indent=2)
    for path, content in updates.items():  # installation.json last: it records the completed switch.
        replace_text(path, content, 0o600 if path == token_path else None)
    return True


def secret(root, name, length=32):
    path = root / 'secrets' / name
    write_new(path, secrets.token_hex(length) + ('\n' if name == 'setup-token' else ''))
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


def configure(root, domain, email, tls_mode="http", runner_image="ghcr.io/rzvdaniel/automaton-runner:latest"):
    if not re.fullmatch(r'(?=.{1,180}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}', domain):
        raise ValueError('Use a public domain such as example.com, without a scheme or path.')
    if not re.fullmatch(r'[^\s@]+@[^\s@]+\.[^\s@]+', email):
        raise ValueError('A valid certificate notification email is required.')
    if (root / 'appsettings.Production.json').exists() or (root / 'installation.json').exists():
        required = ['postgres-password', 'rabbitmq-password', 'setup-token',
                    'signing-key.json', 'hmac-key', 'ai-encryption-key', 'runtime-proxy-key', 'cloudflare-token']
        if (root / 'secrets' / 'mysql-password').is_file():
            raise ValueError('This installation runs on MySQL; this release uses PostgreSQL. Nothing was changed. Migrate the data first with migrate-to-postgresql.sh (docs/prod/migrate-to-postgresql.md, issue #587).')
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
    if tls_mode in ('http', 'delegated'):
        write_new(cloudflare, "")
    elif not cloudflare.exists():
        write_new(cloudflare, read_cloudflare_token())
    password = secret(root, 'postgres-password')
    broker_password = secret(root, 'rabbitmq-password')
    setup_token = secret(root, 'setup-token')
    urls = public_urls(domain)
    api, studio, apps, store = urls['api'], urls['studio'], urls['apps'], urls['store']
    resolver = resolver_for(tls_mode)
    settings = {
        'ConnectionStrings': {'PostgreSqlConnection': f'Host=postgres;Database=automaton;Username=automaton;Password={password}'},
        'RabbitMq': dict(Host='rabbitmq', Port=5672, Username='automaton', Password=broker_password,
                         VirtualHost='/', ManagementUrl='http://rabbitmq:15672/'),
        'HostedRunnerRabbitMq': dict(Host=f'api.{domain}', Port=5671, TlsEnabled=True, TlsServerName=f'api.{domain}'),
        'InfrastructureRunnerRabbitMq': dict(Host=f'api.{domain}', Port=5671, TlsEnabled=True, TlsServerName=f'api.{domain}'),
        'Bootstrap': {'Token': setup_token},
        'Cors': {'AllowedOrigins': [studio, api, store]},
        'JwtOptions': dict(ValidateIssuer=True, Issuer=api, ValidateAudience=False, Audience='Automaton.Studio'),
        'AuthenticationSettings': {'RsaSettings': rsa_settings(root), 'HMacSettings': {'SecretKey': secret(root, 'hmac-key')}},
        'AppConfig': dict(ApiUrl=api, WebsiteUrl=studio, UserSignUp=False,
                          AiApiEncryptionKey=secret(root, 'ai-encryption-key', 16)),
        'UserPasswordConfig': dict(RequiredLength=12, RequireDigit=True, RequireUppercase=True,
                                   RequireLowercase=True, RequireNonAlphanumeric=True),
        'CloudflareTurnstile': {'Enabled': False},
        'HostedRunners': {'Enabled': False, 'ServerUrl': api, 'DockerImage': runner_image},
        'Catalog': {'InstallEssentialsOnFirstRunner': True},
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
        'certificatesResolvers': certificates_resolvers(tls_mode, email),
        'api': {'dashboard': False},
    }
    write_new(root / 'traefik.yml', json.dumps(traefik))
    edge = {'http': {'routers': {}, 'services': {
        'api': {'loadBalancer': {'servers': [{'url': 'http://api:8080'}]}},
        'studio': {'loadBalancer': {'servers': [{'url': 'http://studio:80'}]}},
    }}, 'tcp': {'routers': {'broker': {'entryPoints': ['broker'], 'rule': f'HostSNI(`api.{domain}`)',
                                     'service': 'broker', 'tls': {'certResolver': resolver}}},
                'services': {'broker': {'loadBalancer': {'servers': [{'address': 'rabbitmq:5672'}]}}}}}
    for name, rule, service in [('api', f'Host(`api.{domain}`)', 'api'),
                                ('studio', f'Host(`studio.{domain}`)', 'studio'),
                                ('studio-api', f'Host(`studio.{domain}`) && (Path(`/api`) || PathPrefix(`/api/`) || Path(`/auth/github/login`) || PathPrefix(`/signin-github`))', 'api'),
                                ('apps', f'Host(`apps.{domain}`)', 'api')]:
        edge['http']['routers'][name] = dict(entryPoints=['websecure'], rule=rule, service=service,
                                             tls={'certResolver': resolver}, priority=200 if name == 'studio-api' else 1 if name == 'apps' else 100)
    if wildcard_store(tls_mode, domain):
        edge['tls'] = wildcard_store(tls_mode, domain)
    write_new(root / 'dynamic' / 'installation.yml', json.dumps(edge))
    ensure_store(root)
    ensure_studio_paths(root)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--config-dir', required=True, type=Path)
    parser.add_argument('--domain')
    parser.add_argument('--email')
    parser.add_argument('--ensure-store', action='store_true',
                        help='Add the app store to an existing installation without changing credentials.')
    parser.add_argument('--ensure-studio-paths', action='store_true',
                        help='Route Studio pages whose path starts with "api" to Studio on an existing installation.')
    parser.add_argument('--tls-mode', choices=['http', 'cloudflare', 'delegated'], default='http')
    parser.add_argument('--switch-tls', action='store_true',
                        help='Switch an existing installation to --tls-mode; domain, email and credentials are unchanged.')
    parser.add_argument('--cloudflare-token-file', type=Path,
                        help='Read the Cloudflare DNS token from this file instead of a prompt (with --switch-tls).')
    parser.add_argument('--runner-image', default='ghcr.io/rzvdaniel/automaton-runner:latest')
    parser.add_argument('--update-runner-image', nargs=2, metavar=('PREVIOUS', 'CURRENT'),
                        help='Use the release Runner image for new Runners unless an operator chose another image.')
    args = parser.parse_args()
    os.umask(0o077)
    try:
        if args.switch_tls:
            if args.domain or args.email:
                raise ValueError('--switch-tls changes only the TLS mode; the domain and email cannot be changed.')
            print('Switched the TLS mode.' if switch_tls(args.config_dir, args.tls_mode, args.cloudflare_token_file)
                  else f'Already in {args.tls_mode} mode; nothing to change.')
        elif args.update_runner_image:
            previous, current = args.update_runner_image
            if update_runner_image(args.config_dir, previous, current):
                print('New Runners will use the Runner image from this release.')
            else:
                print('Keeping the custom Runner image configured in HostedRunners.DockerImage.')
        elif args.ensure_studio_paths:
            if not (args.config_dir / 'installation.json').is_file():
                raise ValueError('No existing installation configuration was found.')
            ensure_studio_paths(args.config_dir)
        elif args.ensure_store:
            if not (args.config_dir / 'installation.json').is_file():
                raise ValueError('No existing installation configuration was found.')
            ensure_store(args.config_dir)
        elif not args.domain or not args.email:
            parser.error('--domain and --email are required.')
        else:
            configure(args.config_dir, args.domain.lower(), args.email, args.tls_mode, args.runner_image)
    except (ValueError, OSError, KeyError) as error:  # JSONDecodeError is a ValueError.
        parser.exit(1, f'Configuration failed: {error}\n')
