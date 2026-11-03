# PostgreSQL

Updated: 3 November 2026. Status: in progress. Work and status: parent issue [#584](https://github.com/rzvdaniel/automaton/issues/584) and its sub-issues; this file holds the design.

Automaton moves from MySQL to PostgreSQL before the [public launch](public-launch.md), while only our own two servers (test and production) need their data migrated. Decided 3 November 2026.

## Why

- Pomelo, the MySQL provider for EF Core, has no EF Core 10 release and looks abandoned. Oracle's provider breaks CAP's transactional outbox (CAP's MySQL storage needs MySqlConnector). The remaining MySQL option is a single-maintainer fork.
- Npgsql (EF Core provider), `DotNetCore.CAP.PostgreSql` and `Hangfire.PostgreSql` are mature and supported, and Npgsql follows EF Core releases closely.
- After the launch every database change would have to be migrated on servers we do not control.

## Design

- One PostgreSQL baseline migration replaces the MySQL migration history. New installations start on it; existing servers are moved once with the data migration below.
- Case sensitivity: MySQL compares text case-insensitively by default, PostgreSQL does not. Every lookup by email, user name, app name, slug, domain or similar is checked and keeps its current behavior (case-insensitive collation or normalized columns), with tests.
- MySQL-specific SQL (for example `GET_LOCK` for app deletion) becomes the PostgreSQL equivalent (advisory locks).
- Install, update, backup and restore use PostgreSQL (`pg_dump`/`pg_restore`). The encrypted backup download (#484) is built on the new format.
- Data migration: a tested, repeatable procedure that copies all tables from MySQL to PostgreSQL and verifies row counts, keys, logins, apps, secrets (still decryptable) and schedules. Test server first; production only with the owner's go-ahead and a backup.

## Plan

Each step depends on the previous one.

1. **Application on PostgreSQL** (#585, done 3 November 2026) (on .NET 9): providers, baseline migration, raw SQL, case-insensitive lookups, test fixtures. *Acceptance:* all tests pass on PostgreSQL; a fresh database works end to end locally.
2. **Server installation on PostgreSQL** (#586): Compose, install, update, backup and restore scripts, install guide. *Acceptance:* a clean install, update, backup and restore work on PostgreSQL.
3. **Data migration of existing servers** (#587): procedure, verification, test server, then production. *Acceptance:* both servers run on PostgreSQL with all data, users can sign in, apps run, secrets decrypt. *Status:* the procedure ([runbook](../prod/migrate-to-postgresql.md), `docker/install/migrate-to-postgresql.sh`) is built and was rehearsed on a throwaway copy of the test server's database on 3 November 2026: copy and verification (1,126 rows in 86 tables by count and content hash, 96 foreign keys, sequences), identical API answers for an administrator and a user before and after, all protected values decrypt with the original key ring, a deliberate failure returns to MySQL by itself, and `--rollback` restores the old state; API and Worker were down 44 s. The test server itself and production are not switched yet.
4. **.NET 10** (#493, #494): retarget with Npgsql 10 after step 1.

## Step 1 result

What the next steps rely on:

- Packages: Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4, `DotNetCore.CAP.PostgreSql` 10.0.1, `Hangfire.PostgreSql`. The connection string is `ConnectionStrings:PostgreSqlConnection` (API and Worker); the `AppConfig:DatabaseType` setting is gone.
- One baseline migration, `PostgreSqlBaseline`. The first migration run needs `CREATE EXTENSION citext`: the database owner may do it (the extension is trusted since PostgreSQL 13).
- Case-insensitive text: MySQL compared without case, so every identifier-like text column (bounded up to 512 characters, indexed or key columns, and the unbounded `Name`, `DisplayName`, `PublicUsername`, `GitHubUsername`, `Category` and `RegistrationProvider` columns) is `citext`. Longer text (messages, logs, JSON, tokens) is plain `text`. `PostgreSqlModelConfiguration` applies this to the model. Unlike MySQL's `utf8mb4_0900_ai_ci`, `citext` still tells accented letters apart. Data migration: values that differ only by case cannot exist in MySQL unique indexes, so they copy without conflicts.
- Dates: `timestamptz`; the model converts every `DateTime` to UTC on write and reads it back as UTC. JSON columns are `jsonb`, long text is `text`.
- Locks: app deletion and first-administrator setup use session-level advisory locks (`pg_try_advisory_lock`); scheduled-action admission uses `SELECT ... FOR UPDATE` on the user row.
- CAP keeps separate storage per host: schemas `cap_api` and `cap_worker`; Hangfire uses schema `hangfire` (both created at startup).
- Tests: PostgreSQL integration tests read `AUTOMATON_TEST_POSTGRES_CONNECTION_STRING` (a server connection whose user can create databases) and skip without it; CI provides a PostgreSQL 16 service..

