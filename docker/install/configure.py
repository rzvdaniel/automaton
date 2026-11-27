#!/usr/bin/env python3
"""Create installation configuration once; never regenerate existing credentials."""
import argparse
import base64
import getpass
import json
import os
from pathlib import Path
import ipaddress
import re
import secrets
import subprocess
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
    """Traefik certificate resolver of a TLS mode; custom has none (the admin's certificate is a file)."""
    if tls_mode == 'custom':
        return ''
    return {'cloudflare': 'cloudflare', 'delegated': 'delegated'}.get(tls_mode, 'letsencrypt')


def tls_block(resolver):
    return {'certResolver': resolver} if resolver else {}


def host_resolver(tls_mode):
    """Resolver for studio, api, store and the broker. Delegated mode can answer DNS-01 only for apps.<domain> and
    *.apps.<domain> (the one NS record), so every other host gets HTTP-01 as in the default mode."""
    return 'letsencrypt' if tls_mode == 'delegated' else resolver_for(tls_mode)


def custom_domain_resolver(tls_mode):
    return '' if tls_mode == 'custom' else 'letsencrypt'


def challenge_zone(domain):
    return f'_acme-challenge.apps.{domain}'


def certificates_resolvers(tls_mode, email):
    """Traefik certificate resolver for a TLS mode. delegated: Let's Encrypt asks our own DNS responder (#891)."""
    if tls_mode == 'custom':
        return {}
    acme = {'email': email, 'storage': '/certificates/acme.json'}
    if tls_mode == 'http':
        acme['httpChallenge'] = {'entryPoint': 'web'}
    elif tls_mode == 'delegated':
        # The responder is the authority for the challenge name, so there is nothing to wait for.
        acme['dnsChallenge'] = {'provider': 'httpreq', 'propagation': {'disableChecks': True}}
    else:
        acme['dnsChallenge'] = {'provider': 'cloudflare'}
    resolvers = {resolver_for(tls_mode): {'acme': acme}}
    if tls_mode != 'http':
        # An app's own domain is not ours to change DNS for, so it gets HTTP-01.
        resolvers['letsencrypt'] = {'acme': {'email': email, 'storage': '/certificates/acme.json',
                                             'httpChallenge': {'entryPoint': 'web'}}}
    return resolvers


def wildcard_store(tls_mode, domain):
    """Default certificate for *.apps.<domain>; http mode has none (one certificate per app)."""
    if tls_mode in ('http', 'custom'):
        return None
    return {'stores': {'default': {'defaultGeneratedCert': {'resolver': resolver_for(tls_mode),
            'domain': {'main': f'apps.{domain}', 'sans': [f'*.apps.{domain}']}}}}}


def public_urls(domain):
    return {name: f'https://{name}.{domain}' for name in ('api', 'studio', 'apps', 'store')}


HSTS = dict(stsSeconds=31536000, stsIncludeSubdomains=True)


def security_headers_config(domain):
    """Shared response-header middlewares (#1829). 'security-headers' is for Studio, the API and the Store; Studio frames
    apps, so 'app-security-headers' lets only the Studio origin do that."""
    sources = f"https://*.{domain} wss://*.{domain}"
    csp = ("default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; "
           "img-src 'self' data: blob: https:; font-src 'self' data:; worker-src 'self' blob:; "
           f"connect-src 'self' {sources}; frame-src https://*.{domain}; "
           "object-src 'none'; base-uri 'self'; form-action 'self' https:; frame-ancestors 'none'")
    return {'http': {'middlewares': {
        'security-headers': {'headers': dict(HSTS, contentTypeNosniff=True, frameDeny=True,
                                             contentSecurityPolicy=csp)},
        'app-security-headers': {'headers': dict(HSTS, contentTypeNosniff=True,
                                                 contentSecurityPolicy=f'frame-ancestors https://studio.{domain}')},
    }}}


def ensure_security_headers(root):
    """Write the shared header middlewares; ours, so replaced when the content changes."""
    state = json.loads((root / 'installation.json').read_text())
    (root / 'dynamic').mkdir(exist_ok=True, mode=0o700)
    path = root / 'dynamic' / 'security-headers.yml'
    content = json.dumps(security_headers_config(state['domain']))
    if not path.exists() or path.read_text() != content:
        with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, delete=False) as stream:
            stream.write(content)
        os.replace(stream.name, path)
    # Routers the installer wrote earlier lack the middleware; add it. A hand-written (non-JSON) file is left alone.
    edge_path = root / 'dynamic' / 'installation.yml'
    try:
        edge = json.loads(edge_path.read_text())
        routers = edge['http']['routers']
    except (OSError, ValueError, KeyError, TypeError):
        return
    changed = False
    for name in ('api', 'studio', 'studio-api', 'apps'):
        router = routers.get(name)
        if isinstance(router, dict) and not router.get('middlewares'):
            router['middlewares'] = ['app-security-headers@file' if name == 'apps' else 'security-headers@file']
            changed = True
    if changed:
        replace_json(edge_path, edge)


def store_router(domain, resolver):
    return dict(entryPoints=['websecure'], rule=f'Host(`store.{domain}`)', service='store',
                tls=tls_block(resolver), priority=100, middlewares=['security-headers@file'])


def studio_paths_router(domain, resolver):
    # Studio pages whose path starts with "api" (e.g. /apikeys) belong to Studio, not the API. The studio-api router
    # matches PathPrefix(`/api`) as plain text on installations configured before this router existed.
    # Any studio.* host: a server can also answer on extra hostnames (the test server has studio.test.<domain>).
    return dict(entryPoints=['websecure'], rule=r'HostRegexp(`^studio\.`) && PathRegexp(`^/api[^/]`)',
                service='studio-pages', tls=tls_block(resolver), priority=300,
                middlewares=['security-headers@file'])


def ensure_studio_paths(root):
    """Route Studio pages such as /apikeys to Studio; idempotent and safe for existing installations."""
    state = json.loads((root / 'installation.json').read_text())
    resolver = host_resolver(state.get('tls_mode'))
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
    resolver = host_resolver(state.get('tls_mode'))
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
    store_yml = root / 'dynamic' / 'store.yml'
    store_content = json.dumps({'http': {
        'routers': {'store': store_router(domain, resolver)},
        'services': {'store': {'loadBalancer': {'servers': [{'url': 'http://store:80'}]}}},
    }})
    if store_yml.exists() and store_yml.read_text() != store_content and store_yml.read_text().lstrip().startswith('{'):
        store_yml.unlink()  # Ours (JSON); earlier versions lacked the security-headers middleware.
    write_new(store_yml, store_content)


def ensure_custom_domain_resolver(root):
    """Give a Cloudflare-mode installation the HTTP-01 'letsencrypt' resolver that custom domains need.
    Returns 'changed', 'unchanged', or 'skipped' (traefik.yml was adopted or hand-edited; nothing is touched)."""
    state = json.loads((root / 'installation.json').read_text())
    if state.get('tls_mode') != 'cloudflare':
        return 'unchanged'  # http mode already has it
    traefik_path = root / 'traefik.yml'
    settings_path = root / 'appsettings.Production.json'
    try:
        traefik = json.loads(traefik_path.read_text())
        resolvers = traefik['certificatesResolvers']
        settings = json.loads(settings_path.read_text())
        if not isinstance(resolvers, dict) or not isinstance(settings, dict):
            raise ValueError
    except (OSError, ValueError, KeyError):
        return 'skipped'
    changed = False
    if 'letsencrypt' not in resolvers:
        resolvers['letsencrypt'] = {'acme': {'email': state['email'], 'storage': '/certificates/acme.json',
                                             'httpChallenge': {'entryPoint': 'web'}}}
        replace_text(traefik_path, json.dumps(traefik))
        changed = True
    published = settings.setdefault('PublishedApps', {})
    if published.get('CustomDomainCertificateResolver') != 'letsencrypt':
        published['CustomDomainCertificateResolver'] = 'letsencrypt'
        replace_json(settings_path, settings)
        changed = True
    return 'changed' if changed else 'unchanged'


PREVIOUS_ACCESS_LOG = {'filePath': '/certificates/logs/access.log', 'format': 'json',
                       'fields': {'defaultMode': 'keep', 'headers': {'defaultMode': 'drop'}}}
ACCESS_LOG = {'filePath': '/certificates/logs/access.log', 'format': 'json',
              'fields': {'defaultMode': 'keep', 'headers': {'defaultMode': 'drop', 'names': {'X-Automaton-Auth-Failure': 'keep'}}}}


def ensure_access_log(root):
    """Write Traefik's JSON access log (it keeps only the X-Automaton-Auth-Failure response header; the earlier no-header setting is upgraded) into the certificates volume, where the fail2ban jails read it.
    Returns 'changed', 'unchanged', or 'skipped' (traefik.yml was adopted or hand-edited; nothing is touched)."""
    traefik_path = root / 'traefik.yml'
    try:
        traefik = json.loads(traefik_path.read_text())
        if not isinstance(traefik, dict):
            raise ValueError
    except (OSError, ValueError):
        return 'skipped'
    if 'accessLog' in traefik and traefik['accessLog'] != PREVIOUS_ACCESS_LOG:
        return 'unchanged'
    traefik['accessLog'] = ACCESS_LOG
    replace_text(traefik_path, json.dumps(traefik))
    return 'changed'


def ensure_runner_broker_host(root):
    """Hand remote Runners the server's public hostname (with its certificate) instead of a private address.
    Only IP literals, single-label names and an empty host in an existing section are replaced; a hand-set public hostname is kept.
    InfrastructureRunnerRabbitMq (the local Runner) is not touched. Returns 'changed', 'unchanged' or 'skipped'."""
    state = json.loads((root / 'installation.json').read_text())
    settings_path = root / 'appsettings.Production.json'
    try:
        settings = json.loads(settings_path.read_text())
        if not isinstance(settings, dict):
            raise ValueError
    except (OSError, ValueError):
        return 'skipped'
    api = f"api.{state['domain']}"
    broker = settings.get('HostedRunnerRabbitMq')
    if not isinstance(broker, dict):
        return 'unchanged'  # no Runner broker configured: nothing to migrate
    host = str(broker.get('Host') or '').strip()
    try:
        ipaddress.ip_address(host.strip('[]'))
        private = True
    except ValueError:
        private = '.' not in host
    if not private:
        return 'unchanged'
    settings['HostedRunnerRabbitMq'] = dict(broker, Host=api, Port=5671, TlsEnabled=True, TlsServerName=api)
    replace_json(settings_path, settings)
    return 'changed'


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


def set_resolver(node, resolver, apps_resolver=None, name=None):
    """Point every router's certResolver at the resolver, recursively; leave everything else alone.
    The router called "apps" gets apps_resolver when given."""
    if isinstance(node, dict):
        if isinstance(node.get('tls'), dict) and 'certResolver' in node['tls']:
            node['tls']['certResolver'] = apps_resolver if name == 'apps' and apps_resolver else resolver
        for key, value in node.items():
            set_resolver(value, resolver, apps_resolver, key)
    elif isinstance(node, list):
        for value in node:
            set_resolver(value, resolver, apps_resolver, name)


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
    # Delegated is re-applied on itself: servers installed before #1583 have DNS-01 on every host.
    if tls_mode == current and tls_mode != 'delegated' and not (tls_mode == 'cloudflare' and (token_file or not have_token)):
        return False
    resolver, apps_resolver = host_resolver(tls_mode), resolver_for(tls_mode)
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
    set_resolver(edge, resolver, apps_resolver)
    updates = {traefik_path: json.dumps(traefik), edge_path: json.dumps(edge)}
    for name in ('store.yml', 'studio-paths.yml'):
        path = root / 'dynamic' / name
        if path.is_file():
            try:
                extra = json.loads(path.read_text())
            except ValueError:
                raise ValueError(f'dynamic/{name} was not generated by the installer. Nothing was changed.')
            set_resolver(extra, resolver, apps_resolver)
            updates[path] = json.dumps(extra)
    settings_path = root / 'appsettings.Production.json'
    settings = json.loads(settings_path.read_text())
    settings.setdefault('PublishedApps', {})['CertificateResolver'] = apps_resolver
    settings['PublishedApps']['CustomDomainCertificateResolver'] = 'letsencrypt'
    updates[settings_path] = json.dumps(settings, indent=2)
    updates[token_path] = token
    updates[state_path] = json.dumps(dict(state, tls_mode=tls_mode), indent=2)
    changed = False
    for path, content in updates.items():  # installation.json last: it records the completed switch.
        if tls_mode == current and path.is_file() and path.read_text() == content:
            continue
        replace_text(path, content, 0o600 if path == token_path else None)
        changed = True
    return changed


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


def certificate_names(cert_file):
    """DNS names of a PEM certificate's subjectAltName, and whether it is valid now (openssl, no extra packages)."""
    result = subprocess.run(['openssl', 'x509', '-noout', '-ext', 'subjectAltName', '-in', str(cert_file)],
                            capture_output=True, text=True)
    if result.returncode != 0:
        raise ValueError(f'{cert_file} is not a readable PEM certificate.')
    names = re.findall(r'DNS:([^,\s]+)', result.stdout)
    if not names:
        raise ValueError(f'{cert_file} has no subject alternative names.')
    if subprocess.run(['openssl', 'x509', '-noout', '-checkend', '0', '-in', str(cert_file)],
                      capture_output=True).returncode != 0:
        raise ValueError(f'{cert_file} has expired.')
    return [name.lower() for name in names]


def name_covered(name, patterns):
    labels = name.split('.')
    return any(pattern == name or (pattern.startswith('*.') and pattern.split('.')[1:] == labels[1:] and len(labels) > 1)
               for pattern in patterns)


def public_key(args):
    # -passin pass: and a closed stdin make a passphrase-protected key fail at once instead of prompting.
    result = subprocess.run(['openssl', *args], capture_output=True, text=True, stdin=subprocess.DEVNULL)
    if result.returncode != 0:
        raise ValueError('The key file is not a readable PEM private key without a passphrase that belongs to a certificate.')
    return result.stdout


def install_custom_certificate(root, domain, cert_file, key_file):
    """Check the admin's certificate and key and copy them where Traefik reads them (certs/, dynamic/custom-tls.yml)."""
    if not cert_file or not key_file:
        raise ValueError('--tls-mode custom needs --cert-file and --key-file.')
    for path in (cert_file, key_file):
        if not path.is_file():
            raise ValueError(f'{path} was not found.')
    names = certificate_names(cert_file)
    required = [f'{host}.{domain}' for host in ('api', 'studio', 'store', 'apps')] + [f'x.apps.{domain}']
    missing = [name.replace('x.apps.', '*.apps.') for name in required if not name_covered(name, names)]
    if missing:
        raise ValueError('The certificate does not cover: ' + ', '.join(missing) + '. It covers: ' + ', '.join(names) + '.')
    if public_key(['x509', '-noout', '-pubkey', '-in', str(cert_file)]) != public_key(['pkey', '-pubout', '-passin', 'pass:', '-in', str(key_file)]):
        raise ValueError('The key file does not belong to the certificate.')
    certs = root / 'certs'
    certs.mkdir(parents=True, exist_ok=True, mode=0o700)
    replace_text(certs / 'fullchain.pem', cert_file.read_text(), 0o644)
    replace_text(certs / 'privkey.pem', key_file.read_text(), 0o600)
    (root / 'dynamic').mkdir(exist_ok=True, mode=0o700)
    # Rewriting the file makes Traefik reload the certificate files it points to.
    replace_text(root / 'dynamic' / 'custom-tls.yml', json.dumps({'tls': {'certificates': [
        {'certFile': '/etc/traefik/certs/fullchain.pem', 'keyFile': '/etc/traefik/certs/privkey.pem'}]}}), 0o600)


def configure(root, domain, email, tls_mode="http", runner_image="ghcr.io/rzvdaniel/automaton-runner:latest", cert_file=None, key_file=None):
    if not re.fullmatch(r'(?=.{1,180}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}', domain):
        raise ValueError('Use a public domain such as example.com, without a scheme or path.')
    if not re.fullmatch(r'[^\s@]+@[^\s@]+\.[^\s@]+', email):
        raise ValueError('A valid certificate notification email is required.')
    if (root / 'appsettings.Production.json').exists() or (root / 'installation.json').exists():
        required = ['postgres-password', 'rabbitmq-password', 'setup-token',
                    'signing-key.json', 'hmac-key', 'ai-encryption-key', 'runtime-proxy-key', 'cloudflare-token']
        if (root / 'secrets' / 'mysql-password').is_file():
            raise ValueError('This installation runs on MySQL; this release uses PostgreSQL. Nothing was changed. Migrate the data first with migrate-to-postgresql.sh (src/automaton-vue/docs/install.md#move-a-mysql-server-to-postgresql, issue #587).')
        if not (root / 'installation.json').exists() or any(not (root / 'secrets' / name).is_file() for name in required):
            raise ValueError('Existing installation credentials are incomplete. Restore the configuration backup; keys will not be regenerated.')
    state = root / 'installation.json'
    desired = dict(domain=domain, email=email, tls_mode=tls_mode)
    if state.exists() and json.loads(state.read_text()) != desired:
        raise ValueError('This directory belongs to another domain/email. Existing configuration was preserved.')
    if tls_mode == 'custom':
        install_custom_certificate(root, domain, cert_file, key_file)  # Before anything else is written.
    root.mkdir(parents=True, exist_ok=True, mode=0o700)
    (root / 'certs').mkdir(exist_ok=True, mode=0o700)  # Bind-mounted into Traefik in every mode.
    (root / 'secrets').mkdir(exist_ok=True, mode=0o700)
    (root / 'dynamic').mkdir(exist_ok=True, mode=0o700)
    cloudflare = root / 'secrets' / 'cloudflare-token'
    if tls_mode in ('http', 'delegated', 'custom'):
        write_new(cloudflare, "")
    elif not cloudflare.exists():
        write_new(cloudflare, read_cloudflare_token())
    password = secret(root, 'postgres-password')
    broker_password = secret(root, 'rabbitmq-password')
    setup_token = secret(root, 'setup-token')
    urls = public_urls(domain)
    api, studio, apps, store = urls['api'], urls['studio'], urls['apps'], urls['store']
    resolver, apps_resolver = host_resolver(tls_mode), resolver_for(tls_mode)
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
                              RuntimeProxyAuthKey=secret(root, 'runtime-proxy-key'), TraefikDynamicConfigRoot='/app/AppData/traefik-dynamic', RuntimeTunnelServiceName='api', CertificateResolver=apps_resolver, CustomDomainCertificateResolver=custom_domain_resolver(tls_mode)),
        'DataProtection': {'KeysPath': '/app/data-protection-keys'},
        'Logging': {'File': {'RetainedFileCountLimit': 14, 'FileSizeLimitBytes': 10485760}},
        'EmailConfig': {'EmailHost': '', 'EmailAccount': '', 'EmailPassword': ''},
        'GitHub': {'ClientId': '', 'ClientSecret': '', 'ApiBaseUrl': api, 'FrontendUrl': studio},
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
        'api': {'dashboard': False}, 'accessLog': ACCESS_LOG,
    }
    if resolvers := certificates_resolvers(tls_mode, email):
        traefik['certificatesResolvers'] = resolvers
    write_new(root / 'traefik.yml', json.dumps(traefik))
    edge = {'http': {'routers': {}, 'services': {
        'api': {'loadBalancer': {'servers': [{'url': 'http://api:8080'}]}},
        'studio': {'loadBalancer': {'servers': [{'url': 'http://studio:80'}]}},
    }}, 'tcp': {'routers': {'broker': {'entryPoints': ['broker'], 'rule': f'HostSNI(`api.{domain}`)',
                                     'service': 'broker', 'tls': tls_block(resolver)}},
                'services': {'broker': {'loadBalancer': {'servers': [{'address': 'rabbitmq:5672'}]}}}}}
    for name, rule, service in [('api', f'Host(`api.{domain}`)', 'api'),
                                ('studio', f'Host(`studio.{domain}`)', 'studio'),
                                ('studio-api', f'Host(`studio.{domain}`) && (Path(`/api`) || PathPrefix(`/api/`) || Path(`/auth/github/login`) || PathPrefix(`/signin-github`))', 'api'),
                                ('apps', f'Host(`apps.{domain}`)', 'api')]:
        edge['http']['routers'][name] = dict(entryPoints=['websecure'], rule=rule, service=service,
                                             tls=tls_block(apps_resolver if name == 'apps' else resolver), priority=200 if name == 'studio-api' else 1 if name == 'apps' else 100,
                                             middlewares=['app-security-headers@file' if name == 'apps' else 'security-headers@file'])
    if wildcard_store(tls_mode, domain):
        edge['tls'] = wildcard_store(tls_mode, domain)
    write_new(root / 'dynamic' / 'installation.yml', json.dumps(edge))
    ensure_security_headers(root)
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
    parser.add_argument('--ensure-custom-domain-resolver', action='store_true',
                        help='Add the HTTP-01 resolver for custom domains to an existing Cloudflare-mode installation.')
    parser.add_argument('--ensure-access-log', action='store_true',
                        help='Turn on the Traefik JSON access log that the fail2ban jails read, on an existing installation.')
    parser.add_argument('--ensure-runner-broker-host', action='store_true',
                        help='Give remote Runners the public API hostname as broker address if a private address is set.')
    parser.add_argument('--tls-mode', choices=['http', 'cloudflare', 'delegated', 'custom'], default='http')
    parser.add_argument('--cert-file', type=Path, help='PEM certificate chain for --tls-mode custom (or --replace-certificate).')
    parser.add_argument('--key-file', type=Path, help='PEM private key for --tls-mode custom (or --replace-certificate).')
    parser.add_argument('--replace-certificate', action='store_true',
                        help='Swap the certificate of an installation in custom mode (renewal).')
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
        if args.replace_certificate:
            state = json.loads((args.config_dir / 'installation.json').read_text())
            if state.get('tls_mode') != 'custom':
                raise ValueError('This installation does not use --tls-mode custom.')
            install_custom_certificate(args.config_dir, state['domain'], args.cert_file, args.key_file)
            print('Certificate replaced; Traefik reloads it within seconds.')
        elif args.switch_tls:
            if 'custom' in (args.tls_mode, json.loads((args.config_dir / 'installation.json').read_text()).get('tls_mode')):
                raise ValueError('Switching to or from custom certificates is not supported; reinstall or edit the files by hand.')
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
        elif args.ensure_custom_domain_resolver:
            if not (args.config_dir / 'installation.json').is_file():
                raise ValueError('No existing installation configuration was found.')
            result = ensure_custom_domain_resolver(args.config_dir)
            print({'changed': 'Added the custom-domain certificate resolver.',
                   'unchanged': 'Custom-domain certificate resolver already present.',
                   'skipped': 'traefik.yml or appsettings.Production.json was not generated by the installer; '
                              'skipped. Add a letsencrypt HTTP-01 resolver by hand for custom domains.'}[result])
            print('RESULT=' + result)
        elif args.ensure_access_log:
            if not (args.config_dir / 'installation.json').is_file():
                raise ValueError('No existing installation configuration was found.')
            result = ensure_access_log(args.config_dir)
            print({'changed': 'Turned on the Traefik access log.',
                   'unchanged': 'Traefik access log already configured.',
                   'skipped': 'traefik.yml was not generated by the installer; skipped. fail2ban needs a JSON access log at /certificates/logs/access.log.'}[result])
            print('RESULT=' + result)
        elif args.ensure_runner_broker_host:
            if not (args.config_dir / 'installation.json').is_file():
                raise ValueError('No existing installation configuration was found.')
            result = ensure_runner_broker_host(args.config_dir)
            print({'changed': 'Remote Runners now get the public broker address.',
                   'unchanged': 'Remote Runner broker address already public.',
                   'skipped': 'appsettings.Production.json was not generated by the installer; skipped.'}[result])
            print('RESULT=' + result)
        elif args.ensure_studio_paths:
            if not (args.config_dir / 'installation.json').is_file():
                raise ValueError('No existing installation configuration was found.')
            ensure_studio_paths(args.config_dir)
            ensure_security_headers(args.config_dir)
        elif args.ensure_store:
            if not (args.config_dir / 'installation.json').is_file():
                raise ValueError('No existing installation configuration was found.')
            ensure_store(args.config_dir)
            ensure_security_headers(args.config_dir)
        elif not args.domain or not args.email:
            parser.error('--domain and --email are required.')
        else:
            configure(args.config_dir, args.domain.lower(), args.email, args.tls_mode, args.runner_image, args.cert_file, args.key_file)
    except (ValueError, OSError, KeyError) as error:  # JSONDecodeError is a ValueError.
        parser.exit(1, f'Configuration failed: {error}\n')
