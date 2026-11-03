# Migrate a server from MySQL to PostgreSQL

Servers installed before PostgreSQL (their `release.env` has a `MYSQL_IMAGE` line) keep their data in MySQL. Releases that run on PostgreSQL are not applied to them until the data has been moved once. `migrate-to-postgresql.sh` does that: it copies every table, proves the copy, and switches the installation over. MySQL and its data stay untouched until you remove them, so you can go back.

This page covers the standard installation made by `install.sh`. A server with a hand-written Compose layout (such as an adopted production server) needs the script checked against its service and volume names first; do not run it there unreviewed.

## Before you start

- A release that includes the migration (the bundle contains `migrate-to-postgresql.sh`, `mysql_to_postgres.py` and `migration_app_check.py`). The script downloads and verifies it for you.
- At least 10 GiB free disk space, outbound internet access (it pulls the release images and a `python:3.12-slim` container to run the copy), and the server running normally.
- An administrator account, and ideally a regular user's, in the environment: `AUTOMATON_CHECK_EMAIL` / `AUTOMATON_CHECK_PASSWORD` and optionally `AUTOMATON_CHECK_USER_EMAIL` / `AUTOMATON_CHECK_USER_PASSWORD`. The script signs in as them before and after and requires the API to give the same answers. (`--skip-app-check` skips this; do not on a real server.)
- A quiet moment. API and Worker are down while the data is copied (about a minute for a small server; it grows with the data). Apps keep running; Runners reconnect when the API is back.
- A hosting-provider snapshot or other off-server copy of the server, taken just before.

## Run it

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/docker/install/migrate-to-postgresql.sh -o migrate-to-postgresql.sh
less migrate-to-postgresql.sh
sudo bash migrate-to-postgresql.sh --version server-vX.Y.Z          # report only; changes nothing
```

The report lists the tables and rows to copy and any work still in flight: queued messages and background jobs, Runner operations that have not completed, and Action executions that are running. Wait until they finish, or accept losing them with `--discard-in-flight`.

```bash
export AUTOMATON_CHECK_EMAIL=... AUTOMATON_CHECK_PASSWORD=...      # an administrator
sudo -E bash migrate-to-postgresql.sh --version server-vX.Y.Z --run --confirm your-domain.example
```

What happens, in order:

1. The API snapshot: what the administrator and the regular user see now (apps, Runners, schedules, projects, API keys, ...).
2. API and Worker stop. The script checks for work that cannot be carried over.
3. A verified recovery backup in `/opt/automaton-server/backups/mysql-to-postgresql-<time>`: the configuration, the API and Worker keys, app data, a MySQL dump, the old release files, with checksums.
4. A new `postgres-password` secret is created and the connection string in `appsettings.Production.json` is switched.
5. PostgreSQL starts and the new API creates the schema in a one-off container that receives no traffic.
6. All tables are copied in one transaction (GUIDs, UTC timestamps, booleans, JSON checked; text that PostgreSQL cannot store is refused), then verified: row counts and a content hash per table, every foreign key for missing parents, every sequence ahead of its highest id.
7. The new API and Worker start, and the API snapshot must match step 1. Public routes are checked.
8. Only now the release files are installed, MySQL is stopped (its volume stays), and its credentials are moved to `/etc/automaton/pre-postgresql/`. Nightly backups continue on PostgreSQL.

Not copied, because the application recreates them: the EF migration history (the PostgreSQL baseline replaces it), CAP messages and Hangfire jobs. Check afterwards that recurring jobs and schedules run.

Any failure before step 7 starts the MySQL services again and exits non-zero; the server is as it was. A column that exists only in MySQL is ignored only if every row holds its default; otherwise the script stops and nothing is lost.

## Check it yourself

After the script finishes, sign in to Studio and look at your apps, Runners, schedules and projects. Run an Action or open an app that reads a secret: secrets are encrypted with the keys in the API key volume, which the migration does not touch. Studio **Admin → Installation → Backups** shows the first PostgreSQL backup.

Developers can also decrypt every protected value with the migrated database and the key ring:
`dotnet run --project scripts/tests/protected-data-check -- <key-ring-dir> "<connection string>"`.

## Go back

```bash
sudo bash /opt/automaton-server/migrate-to-postgresql.sh --rollback --confirm your-domain.example
```

(If the script is no longer there, download it as above and add `--install-dir` / `--config-dir` if you changed them.) It stops PostgreSQL, restores the configuration and the previous release files from the recovery backup, and starts the MySQL services. If the MySQL volume was removed it is recreated from the dump. Everything written to PostgreSQL after the switch is lost. Rolling back is safe to repeat if it is interrupted.

## Clean up

When you are sure (a few days), remove the old database and its recovery data:

```bash
docker rm <the mysql container>; docker volume rm <project>_mysql-data
sudo rm -r /etc/automaton/pre-postgresql /opt/automaton-server/backups/mysql-to-postgresql-*
```

The script prints the exact names at the end.

## Trying it on a copy

`--rehearsal` runs the same procedure without root, registry pulls, public checks or backup timers, for a throwaway installation made from a restored backup under its own Compose project name (`COMPOSE_PROJECT_NAME`). Never use it on a real server.
