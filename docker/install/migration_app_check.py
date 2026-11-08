#!/usr/bin/env python3
"""Ask a server what its people see through the API, save the answers, and compare them with another server's.

Run it against the server before the migration (`--save before.json`) and after it (`--compare before.json`). Each account
signs in and lists what it sees; the ids found must be the same on both sides.

  AUTOMATON_CHECK_EMAIL / AUTOMATON_CHECK_PASSWORD         an administrator account (required)
  AUTOMATON_CHECK_USER_EMAIL / AUTOMATON_CHECK_USER_PASSWORD   a regular account (optional, recommended)
  migration_app_check.py --api-url http://api:8080 --save before.json
  migration_app_check.py --api-url http://api:8080 --compare before.json

Credentials come from the environment, never from arguments. Uses only the standard library, so it runs in any Python 3
container on the installation's network.
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.request

ADMIN_ENDPOINTS = {
    'users': '/api/admin/users',
    'apps': '/api/admin/apps',
    'runners': '/api/runners',
    'schedules': '/api/action-schedules',
    'apikeys': '/api/ApiKeys',
}
USER_ENDPOINTS = {
    'apps': '/api/apps',
    'runners': '/api/runners',
    'schedules': '/api/action-schedules',
    'apikeys': '/api/ApiKeys',
}


def call(base, method, path, token=None, body=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(base.rstrip('/') + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw, status = response.read(), response.status
    except urllib.error.HTTPError as error:
        raw, status = error.read(), error.code
    try:
        return status, json.loads(raw)
    except ValueError:
        return status, None


def items(payload):
    if isinstance(payload, list):
        return payload
    if isinstance(payload, dict):
        for key in ('items', 'data', 'apps', 'users', 'results'):
            if isinstance(payload.get(key), list):
                return payload[key]
    return None


def identify(item):
    for key in ('id', 'Id', 'userId', 'name'):
        if isinstance(item, dict) and item.get(key) is not None:
            return str(item[key])
    return json.dumps(item, sort_keys=True)


def snapshot_account(base, email, password, endpoints):
    status, login = call(base, 'POST', '/api/Account/LoginUser', body={'email': email, 'password': password})
    token = (login or {}).get('accessToken') if status == 200 else None
    if not token:
        return {'signIn': f'failed ({status})'}
    result = {'signIn': 'ok'}
    for name, path in endpoints.items():
        status, payload = call(base, 'GET', path, token)
        listed = items(payload) if status == 200 else None
        result[name] = {'status': status, 'ids': sorted(identify(item) for item in listed) if listed is not None else None}
    return result


def take_snapshot(base):
    snapshot = {'health': call(base, 'GET', '/healthz')[0]}
    snapshot['administrator'] = snapshot_account(
        base, os.environ['AUTOMATON_CHECK_EMAIL'], os.environ['AUTOMATON_CHECK_PASSWORD'], ADMIN_ENDPOINTS)
    if os.environ.get('AUTOMATON_CHECK_USER_EMAIL'):
        snapshot['user'] = snapshot_account(
            base, os.environ['AUTOMATON_CHECK_USER_EMAIL'], os.environ['AUTOMATON_CHECK_USER_PASSWORD'], USER_ENDPOINTS)
    return snapshot


def diff_snapshots(before, after):
    """What differs between two snapshots; an empty list means the server answers the same."""
    problems = []
    if after.get('health') != 200:
        problems.append(f'health check answered {after.get("health")}')
    for account in sorted(set(before) | set(after)):
        if account == 'health':
            continue
        left, right = before.get(account), after.get(account)
        if left is None or right is None:
            problems.append(f'{account}: present in only one snapshot')
            continue
        if right.get('signIn') != 'ok':
            problems.append(f'{account}: sign-in {right.get("signIn")}')
            continue
        for name in sorted(set(left) | set(right)):
            if name == 'signIn':
                continue
            a, b = left.get(name), right.get(name)
            if a != b:
                ids_a, ids_b = (a or {}).get('ids'), (b or {}).get('ids')
                detail = f'status {(a or {}).get("status")} -> {(b or {}).get("status")}'
                if ids_a is not None and ids_b is not None:
                    detail += f', {len(ids_a)} -> {len(ids_b)} items, missing {len(set(ids_a) - set(ids_b))}, new {len(set(ids_b) - set(ids_a))}'
                problems.append(f'{account} {name}: {detail}')
    return problems


def summarize(snapshot):
    lines = [f'health: {snapshot.get("health")}']
    for account in ('administrator', 'user'):
        data = snapshot.get(account)
        if data is None:
            continue
        lines.append(f'{account}: sign-in {data["signIn"]}')
        for name, entry in data.items():
            if name != 'signIn':
                found = 'n/a' if entry['ids'] is None else len(entry['ids'])
                lines.append(f'  {name}: status {entry["status"]}, {found} found')
    return lines


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--api-url', required=True)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--save', metavar='FILE')
    group.add_argument('--compare', metavar='FILE')
    args = parser.parse_args(argv)
    if not (os.environ.get('AUTOMATON_CHECK_EMAIL') and os.environ.get('AUTOMATON_CHECK_PASSWORD')):
        parser.error('Set AUTOMATON_CHECK_EMAIL and AUTOMATON_CHECK_PASSWORD to an administrator account.')
    snapshot = take_snapshot(args.api_url)
    for line in summarize(snapshot):
        print(line)
    if args.save:
        with open(args.save, 'w') as stream:
            json.dump(snapshot, stream, indent=1, sort_keys=True)
        print(f'saved to {args.save}')
        return 0 if snapshot['administrator']['signIn'] == 'ok' else 1
    with open(args.compare) as stream:
        problems = diff_snapshots(json.load(stream), snapshot)
    for problem in problems:
        print('FAIL ' + problem)
    print('the server answers the same as before' if not problems else f'{len(problems)} difference(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
