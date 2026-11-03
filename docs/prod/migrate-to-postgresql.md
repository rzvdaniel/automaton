# Migrate a server from MySQL to PostgreSQL

Servers installed before PostgreSQL (their `release.env` has a `MYSQL_IMAGE` line) keep their data in MySQL. Releases that run on PostgreSQL are not applied to them until the data has been moved once. `migrate-to-postgresql.sh` does that: it copies every table, checks the copy, and switches the installation over. The MySQL volume is never modified, so you can go back.

This page covers the standard installation made by `install.sh`. A server with a hand-written Compose layout (such as an adopted production server) needs the script checked against its service and volume names first; do not run it there unreviewed. An installation with a `compose.override.yaml` needs `--allow-override` after you have checked that the override sets no database connection and mentions no MySQL service.

## Before you start

- A release that includes the migration (its bundle contains `migrate-to-postgresql.sh`, `mysql_to_postgres.py` and `migration_app_check.py`). The script downloads and verifies it for you.
- At least 10 GiB free on the installation's filesystem and on Docker's data directory, outbound internet access (it pulls the release images and a `python:3.12-slim` container used for the copy), and a server that is running normally.
- An administrator account, and ideally a regular user's, in the environment: `AUTOMATON_CHECK_EMAIL` / `AUTOMATON_CHECK_PASSWORD` and optionally `AUTOMATON_CHECK_USER_EMAIL` / `AUTOMATON_CHECK_USER_PASSWORD`. The script signs in as them before and after and requires the API to list the same items (`--skip-app-check` skips this; do not on a real server).
- A quiet moment, and `tmux` or `screen`: API and Worker are down while the data is copied (about a minute for a small server; it grows with the data). Apps run on their Runners and are not restarted by the migration; Runners and Studio reconnect when the API is back.
- A hosting-provider snapshot or other off-server copy of the server, taken just before.
- **Rehearse first.** Restore a recent backup onto a scratch machine (or a second Compose project name with `--rehearsal`, never on the real server) and run the whole migration there. The copy converts and checks every row only while the services are down, so a problem in your data (text PostgreSQL cannot store, a column that exists only in MySQL and holds data) shows up during the real downtime otherwise. It is safe, since the script then returns to MySQL, but it costs the window.

## Run it

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/docker/install/migrate-to-postgresql.sh -o migrate-to-postgresql.sh
less migrate-to-postgresql.sh
sudo bash migrate-to-postgresql.sh --version server-vX.Y.Z          # report only; changes nothing
```

The report lists the tables and rows to copy and any work still in flight: queued messages and background jobs, Runner operations that have not completed, and Action executions that are running. Wait until they finish, or accept losing them with `--discard-in-flight`. The check does not look into RabbitMQ: messages that Runners queue while the API is down are not covered, so choose a moment when Runners are quiet.

```bash
export AUTOMATON_CHECK_EMAIL=... AUTOMATON_CHECK_PASSWORD=...      # an administrator
sudo -E bash migrate-to-postgresql.sh --version server-vX.Y.Z --run --confirm your-domain.example
```

What happens, in order:

1. The checks, and an API snapshot of what the administrator and the regular user see now.
2. API and Worker stop (the downtime starts), and the pending work is checked again.
3. A verified recovery backup in `/opt/automaton-server/backups/mysql-to-postgresql-<time>`: the configuration, the API and Worker keys, app data, a MySQL dump, the old release files, with checksums.
4. A `postgres-password` secret is created if there is none and the connection string in `appsettings.Production.json` is switched (other settings are kept). PostgreSQL starts and the new API creates the schema in a one-off container that receives no traffic.
5. All tables are copied in one transaction (GUIDs, UTC timestamps, booleans, JSON are converted and checked; text PostgreSQL cannot store is refused), then verified: row counts and a content hash per table, every foreign key for missing parents, every sequence ahead of its highest id.
6. The new API and Worker start (the switch). The API snapshot must match step 1, and the public routes `https://studio.<domain>/api/install/status` and `/` must answer (the server must reach its own HTTPS endpoint).
7. The release files replace the MySQL ones, MySQL is stopped (its volume stays), and its credentials are moved to `/etc/automaton/pre-postgresql/`. Nightly backups continue on PostgreSQL.

Not copied, because the application recreates them: the EF migration history (the PostgreSQL baseline replaces it), CAP messages and Hangfire jobs. Afterwards, check that recurring jobs and schedules run.

A column that exists only in MySQL is ignored only if every row holds its default; otherwise the script stops and nothing is lost.

### If it stops

- **Before step 6:** the script starts the MySQL services again with the saved configuration and release files, removes the files it added to the configuration directory and the PostgreSQL volume it created, and exits non-zero. If that return itself fails it says so and keeps the recovery data; fix the reported problem and run `--rollback`, which is safe to repeat.
- **After step 6:** the PostgreSQL stack is running and the installation files are still the MySQL ones. Fix what the message reports and run `--finish` (it repeats the API comparison and the public checks, then does step 7). Running `--run` again is refused, so the old MySQL data cannot overwrite the PostgreSQL data. Or run `--rollback`.
- **If your connection drops** or the machine restarts during the downtime: API and Worker may be stopped. Run `sudo bash /opt/automaton-server/migrate-to-postgresql.sh --rollback` (or `--finish` if the state is `switched`; the recovery directory's `STATE` file says which).

## Check it yourself

After the script finishes, sign in to Studio and look at your apps, Runners, schedules and projects. The script's comparison checks that the same items are listed for the accounts you gave it (the first page of each list, ids only), so also open an app, and run an Action that reads a secret: secrets are encrypted with the keys in the API key volume, which the migration does not touch, and only a real run proves they decrypt. Studio **Admin → Installation → Backups** shows the first PostgreSQL backup.

Developers can decrypt every protected value of the migrated database with the key ring:
`dotnet run --project scripts/tests/protected-data-check -- <key-ring-dir> "<connection string>"`.

## Go back

```bash
sudo bash /opt/automaton-server/migrate-to-postgresql.sh --rollback --confirm your-domain.example
```

(Add `--install-dir` / `--config-dir` if you changed them.) It stops PostgreSQL, removes the PostgreSQL volume it created, restores the configuration and the previous release files from the recovery backup, and starts the MySQL services. If the MySQL volume is gone it is recreated from the dump. Everything written to PostgreSQL after the switch is lost. With nothing in progress it says so and does nothing.

## Clean up

When you are sure (a few days), remove the old database and its recovery data:

```bash
docker rm <the mysql container>; docker volume rm <project>_mysql-data
sudo rm -r /etc/automaton/pre-postgresql /opt/automaton-server/backups/mysql-to-postgresql-*
```

The script prints the exact names at the end.

## Trying it on a copy

`--rehearsal` runs the same procedure without root, registry pulls, public checks, the disk check or backup timers, for a throwaway installation restored from a backup under its own Compose project name (`COMPOSE_PROJECT_NAME`). Never use it on a real server.
