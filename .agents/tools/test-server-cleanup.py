#!/usr/bin/env python3
"""Remove what agents created on the TEST server: apps, projects, API keys and users whose name (users: name or
email) starts with "e2e-".

Never touches the test accounts themselves (users are removed only by the ADMIN sign-in, never a SysAdmin or a
configured test account), or anything without the prefix. Never point it at production.

Usage:
  python .agents/tools/test-server-cleanup.py                 # dry run: list what would be removed
  python .agents/tools/test-server-cleanup.py --apply         # remove it
  python .agents/tools/test-server-cleanup.py --apply --older-than-hours 24   # nightly timer

Credentials come from the agent credentials file (default ~/.automaton-test/accounts.env, or
~/.config/automaton-test/accounts.env on the test server). It signs in as each test account (ADMIN, USER, USER2)
and removes only that account's own items. It prints names and ids, never emails, passwords or tokens.
"""
import argparse, datetime, json, os, sys, time, urllib.error, urllib.request

PREFIX = "e2e-"
ACCOUNTS = ("ADMIN", "USER", "USER2")
DEFAULT_FILES = ("~/.automaton-test/accounts.env", "~/.config/automaton-test/accounts.env")


def load(path):
    values = {}
    for line in open(os.path.expanduser(path), encoding="utf8"):
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, value = line.split("=", 1)
            values[key.strip()] = value.strip()
    return values


class Api:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.token = None

    def call(self, method, path, body=None):
        headers = {"Content-Type": "application/json"}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        data = None if body is None else json.dumps(body).encode()
        for attempt in range(4):
            request = urllib.request.Request(self.base + path, data=data, method=method, headers=headers)
            try:
                with urllib.request.urlopen(request, timeout=60) as response:
                    raw = response.read()
                    return response.status, (json.loads(raw) if raw.strip() else None)
            except urllib.error.HTTPError as error:
                if error.code == 429 and attempt < 3:  # login rate limit: honour Retry-After
                    time.sleep(int(error.headers.get("Retry-After") or 60))
                    continue
                return error.code, None
        return 429, None

    def login(self, values, who):
        email = values.get(f"AUTOMATON_TEST_{who}_EMAIL")
        password = values.get(f"AUTOMATON_TEST_{who}_PASSWORD")
        if not email or not password:
            return False
        self.token = None
        status, body = self.call("POST", "/api/Account/LoginUser", {"email": email, "password": password})
        self.token = (body or {}).get("accessToken") if status == 200 else None
        return bool(self.token)


def old_enough(stamp, cutoff):
    if cutoff is None:
        return True
    if not stamp:
        return False  # unknown age: leave it for a run without --older-than-hours
    when = datetime.datetime.fromisoformat(stamp.rstrip("Z").split(".")[0])
    return when <= cutoff


def items(api, path):
    status, body = api.call("GET", path)
    if status != 200:
        return []
    return body if isinstance(body, list) else (body or {}).get("items", [])


def tagged(name):
    return (name or "").lower().startswith(PREFIX)


def remove_app(api, app):
    """Order matters: the Runner only cleans up an app while its owner can still use the Runner (often through a
    project), and the API refuses to delete an app that is still in a project."""
    app_id = app["id"]
    for target in items(api, f"/api/apps/{app_id}/targets"):
        if target.get("runner"):
            api.call("POST", f"/api/apps/{app_id}/targets/{target['id']}/assign-runner",
                     {"runnerId": None, "cleanupDeployedRuntime": True})
    for project_id in app.get("links", []):
        api.call("DELETE", f"/api/projects/{project_id}/apps/{app_id}")
    status, _ = api.call("DELETE", f"/api/apps/{app_id}")
    return status


def clean(api, who, cutoff, apply):
    projects = [p for p in items(api, "/api/projects")  # deleted projects stay listed as inactive
                if p.get("isActive", True) and (p.get("role") == "ProjectAdmin" or who == "ADMIN")]
    apps = {a["id"]: dict(a, links=[]) for a in items(api, "/api/apps")}
    for project in projects:  # project apps don't always appear in /api/apps
        for app in items(api, f"/api/projects/{project['id']}/apps"):
            app_id = app.get("appId") or app.get("id")
            if not app_id:
                continue
            entry = apps.setdefault(app_id, {"id": app_id, "name": app.get("name") or app.get("appName"),
                                             "updatedAtUtc": app.get("updatedAtUtc") or app.get("addedAt"), "links": []})
            entry["links"].append(project["id"])
            if tagged(project.get("name")):
                entry["_fromProject"] = True
    plan = [("app", a, a.get("name"), a["id"]) for a in apps.values()
            if (tagged(a.get("name")) or a.get("_fromProject")) and old_enough(a.get("updatedAtUtc"), cutoff)]
    plan += [("project", f"/api/projects/{p['id']}", p.get("name"), p["id"]) for p in projects
             if tagged(p.get("name")) and old_enough(p.get("updatedAt"), cutoff)]
    plan += [("API key", f"/api/apikeys/{k['id']}", k.get("name"), k["id"]) for k in items(api, "/api/apikeys")
             if tagged(k.get("name")) and old_enough(k.get("createdAt"), cutoff)]
    failures = 0
    for kind, path, name, ident in plan:
        if not apply:
            print(f"  would remove {kind} {name} ({ident})")
            continue
        if kind == "app":
            status = remove_app(api, path)
        else:
            status, _ = api.call("DELETE", path)
        ok = status in (200, 202, 204, 404)
        failures += not ok
        print(f"  {'removed' if ok else f'FAILED ({status})'} {kind} {name} ({ident})")
    if not plan:
        print("  nothing to remove")
    return failures


def clean_users(api, values, cutoff, apply):
    """Hard-delete e2e- users (administrator only). Skips the configured test accounts and system administrators."""
    keep = {values.get(f"AUTOMATON_TEST_{w}_EMAIL", "").lower()
            for w in ("ADMIN", "USER", "USER2", "PLAINADMIN")} - {""}
    status, body = api.call("GET", "/api/admin/users?page=1&pageSize=100&search=" + PREFIX)
    users = (body or {}).get("items", []) if status == 200 else []
    plan = [u for u in users
            if (tagged(u.get("name")) or tagged(u.get("email")))
            and (u.get("email") or "").lower() not in keep
            and not {"SysAdmin", "Admin"} & set(u.get("roles") or [])  # the server refuses to delete administrators
            and old_enough(u.get("registeredAtUtc"), cutoff)]
    failures = 0
    for user in plan:
        label = f"user {user.get('name') or user.get('email')} ({user['id']})"
        if not apply:
            print(f"  would remove {label}")
            continue
        status, _ = api.call("DELETE", f"/api/admin/users/{user['id']}/hard", {"confirmationText": "DELETE"})
        if status == 400:  # the API refuses to delete a user who ever had a project membership: disable instead
            status, _ = api.call("POST", f"/api/admin/users/{user['id']}/disable")
            print(f"  {'disabled (has project memberships)' if status in (200, 204) else f'FAILED ({status})'} {label}")
            failures += status not in (200, 204)
            continue
        ok = status in (200, 202, 204, 404)
        failures += not ok
        print(f"  {'removed' if ok else f'FAILED ({status})'} {label}")
    if not plan:
        print("  no e2e- users")
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="remove; without it, only list")
    parser.add_argument("--older-than-hours", type=float, help="only items not changed for this long")
    parser.add_argument("--credentials", help="credentials file")
    args = parser.parse_args()
    path = args.credentials or next((f for f in DEFAULT_FILES if os.path.exists(os.path.expanduser(f))), None)
    if not path:
        sys.exit("No credentials file found.")
    values = load(path)
    base = values.get("AUTOMATON_TEST_API_URL", "")
    if not base or "test" not in base and "runner-01" not in base:
        sys.exit("AUTOMATON_TEST_API_URL does not look like the test server; refusing to run.")
    cutoff = None
    if args.older_than_hours is not None:
        cutoff = datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None) - datetime.timedelta(hours=args.older_than_hours)
    api, failures = Api(base), 0
    for who in ACCOUNTS:
        if not api.login(values, who):
            print(f"{who}: skipped (no credentials or sign-in failed)")
            continue
        print(f"{who}:")
        failures += clean(api, who, cutoff, args.apply)
    if api.login(values, "ADMIN"):
        print("ADMIN (users):")
        failures += clean_users(api, values, cutoff, args.apply)
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
