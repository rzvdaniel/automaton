#!/usr/bin/env python3
"""Copy an Automaton server database from MySQL to the PostgreSQL baseline schema, and prove the copy.

Subcommands (connection settings come from the environment, never from arguments, so passwords stay out of process lists):

  inspect  read-only report of the source: row counts and work that is still in flight
  copy     replace the data of the PostgreSQL schema with the MySQL data, in one transaction
  verify   compare both sides: row counts, a content hash per table, keys, sequences

  MYSQL_HOST MYSQL_PORT MYSQL_USER MYSQL_PASSWORD MYSQL_DATABASE
  PG_HOST PG_PORT PG_USER PG_PASSWORD PG_DATABASE

The pure rules (value conversion, table and column mapping, comparison) need only the standard library; the database
drivers (PyMySQL, psycopg) are imported when a subcommand connects.
"""
import argparse
import datetime
import decimal
import hashlib
import json
import os
import sys
import uuid

INTEGER_TYPES = {'integer', 'bigint', 'smallint'}
FLOAT_TYPES = {'real', 'double precision'}
TEXT_TYPES = {'text', 'citext', 'character varying', 'character'}
JSON_TYPES = {'json', 'jsonb'}
TIMESTAMPTZ = 'timestamp with time zone'
FRAMEWORK_TABLES = {'__EFMigrationsHistory'}
FRAMEWORK_PREFIXES = ('cap_', 'hangfire.')


class MigrationError(Exception):
    """A problem that stops the migration before anything is committed."""


def is_excluded_source_table(name):
    """Framework state that the baseline schema replaces or recreates: EF history, CAP messages and Hangfire jobs."""
    return name in FRAMEWORK_TABLES or name.startswith(FRAMEWORK_PREFIXES)


# ---- value conversion -------------------------------------------------------------------------------------------

def _json_has_nul(node):
    if isinstance(node, str):
        return '\x00' in node
    if isinstance(node, list):
        return any(_json_has_nul(item) for item in node)
    if isinstance(node, dict):
        return any('\x00' in key or _json_has_nul(item) for key, item in node.items())
    return False


def _convert_uuid(value, where):
    try:
        if isinstance(value, bytes) and len(value) == 16:
            return uuid.UUID(bytes=value)
        return uuid.UUID(str(value))
    except ValueError as error:
        raise MigrationError(f'{where}: "{value}" is not a GUID') from error


def _convert_timestamp(value, where):
    if not isinstance(value, datetime.datetime):
        raise MigrationError(f'{where}: expected a date and time, got {type(value).__name__}')
    if value.tzinfo is not None:
        raise MigrationError(f'{where}: the source value already has a time zone; MySQL values are expected to be UTC without one')
    return value.replace(tzinfo=datetime.timezone.utc)


def _convert_boolean(value, where):
    if isinstance(value, bool):
        return value
    if value in (0, 1):
        return bool(value)
    raise MigrationError(f'{where}: {value!r} is not a valid boolean (expected 0 or 1)')


def _convert_json(value, where):
    text = value.decode('utf-8') if isinstance(value, bytes) else value
    try:
        parsed = json.loads(text)
    except (TypeError, ValueError) as error:
        raise MigrationError(f'{where}: not valid JSON ({error})') from error
    if _json_has_nul(parsed):
        raise MigrationError(f'{where}: the JSON contains a null character (\\u0000), which PostgreSQL cannot store')
    return text


def _convert_text(value, where):
    if not isinstance(value, str):
        raise MigrationError(f'{where}: expected text, got {type(value).__name__}')
    if '\x00' in value:
        raise MigrationError(f'{where}: the text contains a null character, which PostgreSQL cannot store')
    return value


def convert(value, pg_type, where):
    """Convert one MySQL value to the Python value to write to a PostgreSQL column of the given type."""
    if value is None:
        return None
    if pg_type == 'uuid':
        return _convert_uuid(value, where)
    if pg_type == TIMESTAMPTZ:
        return _convert_timestamp(value, where)
    if pg_type == 'boolean':
        return _convert_boolean(value, where)
    if pg_type in JSON_TYPES:
        return _convert_json(value, where)
    if pg_type in TEXT_TYPES:
        return _convert_text(value, where)
    if pg_type in INTEGER_TYPES:
        return int(value)
    if pg_type == 'numeric':
        return decimal.Decimal(value)
    if pg_type in FLOAT_TYPES:
        return float(value)
    if pg_type == 'bytea':
        return bytes(value)
    raise MigrationError(f'{where}: no conversion is defined for the PostgreSQL type {pg_type}')


# ---- table and column mapping -----------------------------------------------------------------------------------

def plan_columns(table, source_columns, target_columns):
    """Columns to copy, in source order. Nothing may be lost, and nothing required may be left empty.

    target_columns maps a name to (pg_type, nullable, has_default).
    """
    for column in source_columns:
        if column not in target_columns:
            raise MigrationError(f'{table}.{column} exists in MySQL but not in the PostgreSQL schema; its data would be lost')
    for column, (_type, nullable, has_default) in target_columns.items():
        if column not in source_columns and not nullable and not has_default:
            raise MigrationError(f'{table}.{column} is required in PostgreSQL but has no value in MySQL')
    return list(source_columns)


# ---- comparison -------------------------------------------------------------------------------------------------

def _plain_number(value):
    return format(decimal.Decimal(str(value)).normalize(), 'f')


def _canonical_json(value):
    text = value.decode('utf-8') if isinstance(value, bytes) else value
    parsed = json.loads(text, parse_float=decimal.Decimal) if isinstance(text, str) else text

    def plain(node):
        if isinstance(node, decimal.Decimal):
            return _plain_number(node)
        if isinstance(node, list):
            return [plain(item) for item in node]
        if isinstance(node, dict):
            return {key: plain(item) for key, item in node.items()}
        return node
    return json.dumps(plain(parsed), sort_keys=True, separators=(',', ':'), ensure_ascii=False)


def canonical(value, pg_type):
    """A string that is equal for equal values however MySQL or PostgreSQL returned them."""
    if value is None:
        return 'N'
    if pg_type == 'uuid':
        text = str(_convert_uuid(value, 'value'))
    elif pg_type == TIMESTAMPTZ:
        moment = value if value.tzinfo is not None else value.replace(tzinfo=datetime.timezone.utc)
        text = moment.astimezone(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%f')
    elif pg_type == 'boolean':
        text = '1' if value else '0'
    elif pg_type in JSON_TYPES:
        text = _canonical_json(value)
    elif pg_type == 'numeric':
        text = _plain_number(value)
    elif pg_type in FLOAT_TYPES:
        text = f'{float(value):.6g}'
    elif pg_type in INTEGER_TYPES:
        text = str(int(value))
    elif pg_type == 'bytea':
        text = bytes(value).hex()
    else:
        text = str(value)
    return 'V:' + text


def row_digest(values, pg_types):
    row = '\x1f'.join(canonical(value, pg_type) for value, pg_type in zip(values, pg_types))
    return int.from_bytes(hashlib.md5(row.encode('utf-8')).digest()[:8], 'big')


# ---- database access --------------------------------------------------------------------------------------------

def connect_mysql():
    import pymysql
    return pymysql.connect(
        host=os.environ['MYSQL_HOST'], port=int(os.environ.get('MYSQL_PORT', '3306')), user=os.environ['MYSQL_USER'],
        password=os.environ['MYSQL_PASSWORD'], database=os.environ['MYSQL_DATABASE'], charset='utf8mb4',
        init_command="SET time_zone='+00:00'", autocommit=True)


def connect_postgres():
    import psycopg
    return psycopg.connect(
        host=os.environ['PG_HOST'], port=int(os.environ.get('PG_PORT', '5432')), user=os.environ['PG_USER'],
        password=os.environ['PG_PASSWORD'], dbname=os.environ['PG_DATABASE'])


def source_tables(my):
    with my.cursor() as cursor:
        cursor.execute('SHOW FULL TABLES')
        names = sorted(name for name, kind in cursor.fetchall() if kind == 'BASE TABLE')
    return [name for name in names if not is_excluded_source_table(name)]


def source_columns(my, table):
    with my.cursor() as cursor:
        cursor.execute('SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() '
                       'AND table_name = %s ORDER BY ordinal_position', (table,))
        return [row[0] for row in cursor.fetchall()]


def target_tables(pg):
    rows = pg.execute("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' "
                      "AND table_type = 'BASE TABLE' ORDER BY table_name").fetchall()
    return [name for (name,) in rows if name not in FRAMEWORK_TABLES]


def target_columns(pg, table):
    rows = pg.execute(
        "SELECT column_name, CASE WHEN data_type = 'USER-DEFINED' THEN udt_name ELSE data_type END, is_nullable = 'YES', "
        "column_default IS NOT NULL OR is_identity = 'YES' OR is_generated = 'ALWAYS' FROM information_schema.columns "
        "WHERE table_schema = 'public' AND table_name = %s ORDER BY ordinal_position", (table,)).fetchall()
    return {name: (pg_type, nullable, has_default) for name, pg_type, nullable, has_default in rows}


def quote_my(name):
    return '`' + name.replace('`', '``') + '`'


def quote_pg(name):
    return '"' + name.replace('"', '""') + '"'


def check_table_sets(my_tables, pg_tables):
    missing_target = sorted(set(my_tables) - set(pg_tables))
    missing_source = sorted(set(pg_tables) - set(my_tables))
    if missing_target:
        raise MigrationError('MySQL tables with no PostgreSQL table (their data would be lost): ' + ', '.join(missing_target))
    if missing_source:
        raise MigrationError('PostgreSQL tables with no MySQL table (the schema is newer than the data): ' + ', '.join(missing_source))


def reset_sequences(pg):
    rows = pg.execute(
        "SELECT table_name, column_name, pg_get_serial_sequence(format('%I.%I', table_schema, table_name), column_name) "
        "FROM information_schema.columns WHERE table_schema = 'public' AND (is_identity = 'YES' OR column_default LIKE 'nextval(%')").fetchall()
    count = 0
    for table, column, sequence in rows:
        if sequence is None:
            continue
        pg.execute(f'SELECT setval(%s, COALESCE((SELECT MAX({quote_pg(column)}) FROM {quote_pg(table)}), 0) + 1, false)', (sequence,))
        count += 1
    return count


def copy_table(my, pg, table, log):
    source = source_columns(my, table)
    target = target_columns(pg, table)
    columns = plan_columns(table, source, target)
    select = f'SELECT {", ".join(quote_my(c) for c in columns)} FROM {quote_my(table)}'
    statement = f'COPY {quote_pg(table)} ({", ".join(quote_pg(c) for c in columns)}) FROM STDIN'
    types = [target[c][0] for c in columns]
    import pymysql.cursors
    rows = 0
    with my.cursor(pymysql.cursors.SSCursor) as reader, pg.cursor() as writer:
        reader.execute(select)
        with writer.copy(statement) as copy:
            for row in reader:
                rows += 1
                try:
                    copy.write_row([convert(value, pg_type, f'{table}.{column}') for value, pg_type, column in zip(row, types, columns)])
                except MigrationError as error:
                    raise MigrationError(f'row {rows} of {table}: {error}') from error
    log(f'  {table}: {rows} rows')
    return rows


def run_copy(log=print):
    my = connect_mysql()
    with connect_postgres() as pg:
        if not pg.execute('SELECT rolsuper FROM pg_roles WHERE rolname = current_user').fetchone()[0]:
            raise MigrationError('The PostgreSQL user must be a superuser to load data with constraint triggers off (the installation user is).')
        tables = source_tables(my)
        check_table_sets(tables, target_tables(pg))
        log(f'Copying {len(tables)} tables')
        with pg.transaction():
            pg.execute('SET LOCAL session_replication_role = replica')
            pg.execute('TRUNCATE ' + ', '.join(quote_pg(t) for t in tables) + ' RESTART IDENTITY CASCADE')
            total = sum(copy_table(my, pg, table, log) for table in tables)
            sequences = reset_sequences(pg)
        log(f'Committed {total} rows in {len(tables)} tables; {sequences} sequences reset')
    my.close()
    return total


# ---- verification -----------------------------------------------------------------------------------------------

def table_fingerprint(rows, pg_types):
    count = total = mixed = 0
    for row in rows:
        digest = row_digest(row, pg_types)
        count += 1
        total = (total + digest) % (1 << 64)
        mixed ^= digest
    return count, total, mixed


def source_fingerprint(my, table, columns, pg_types):
    import pymysql.cursors
    with my.cursor(pymysql.cursors.SSCursor) as cursor:
        cursor.execute(f'SELECT {", ".join(quote_my(c) for c in columns)} FROM {quote_my(table)}')
        return table_fingerprint(cursor, pg_types)


def target_fingerprint(pg, table, columns, pg_types):
    select = ', '.join((quote_pg(c) + '::text') if t in JSON_TYPES else quote_pg(c) for c, t in zip(columns, pg_types))
    with pg.cursor(name='verify_' + hashlib.md5(table.encode()).hexdigest()[:12]) as cursor:
        cursor.execute(f'SELECT {select} FROM {quote_pg(table)}')
        return table_fingerprint(cursor, pg_types)


def orphan_checks(pg):
    """Foreign keys whose child rows point at a parent that is not there (the copy ran with constraint triggers off)."""
    constraints = pg.execute(
        "SELECT c.conname, c.conrelid::regclass::text, c.confrelid::regclass::text, "
        "(SELECT array_agg(a.attname ORDER BY k.ord) FROM unnest(c.conkey) WITH ORDINALITY k(attnum, ord) "
        "   JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum), "
        "(SELECT array_agg(a.attname ORDER BY k.ord) FROM unnest(c.confkey) WITH ORDINALITY k(attnum, ord) "
        "   JOIN pg_attribute a ON a.attrelid = c.confrelid AND a.attnum = k.attnum) "
        "FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE c.contype = 'f' AND n.nspname = 'public' "
        "ORDER BY 1").fetchall()
    problems = []
    for name, child, parent, child_columns, parent_columns in constraints:
        present = ' AND '.join(f'c.{quote_pg(col)} IS NOT NULL' for col in child_columns)
        match = ' AND '.join(f'p.{quote_pg(pc)} = c.{quote_pg(cc)}' for cc, pc in zip(child_columns, parent_columns))
        orphans = pg.execute(f'SELECT count(*) FROM {child} c WHERE {present} AND NOT EXISTS (SELECT 1 FROM {parent} p WHERE {match})').fetchone()[0]
        if orphans:
            problems.append(f'{name}: {orphans} rows in {child} point at a missing row in {parent}')
    return len(constraints), problems


def sequence_problems(pg):
    rows = pg.execute(
        "SELECT table_name, column_name, pg_get_serial_sequence(format('%I.%I', table_schema, table_name), column_name) "
        "FROM information_schema.columns WHERE table_schema = 'public' AND (is_identity = 'YES' OR column_default LIKE 'nextval(%')").fetchall()
    problems, checked = [], 0
    for table, column, sequence in rows:
        if sequence is None:
            continue
        checked += 1
        last_value, is_called = pg.execute(f'SELECT last_value, is_called FROM {sequence}').fetchone()
        following = last_value + 1 if is_called else last_value
        highest = pg.execute(f'SELECT COALESCE(MAX({quote_pg(column)}), 0) FROM {quote_pg(table)}').fetchone()[0]
        if following <= highest:
            problems.append(f'{table}.{column}: the next value {following} is not above the highest {highest}')
    return checked, problems


def run_verify(log=print):
    my = connect_mysql()
    failures = []
    with connect_postgres() as pg:
        tables = source_tables(my)
        check_table_sets(tables, target_tables(pg))
        log(f'{"table":42} {"rows":>8}  result')
        total = 0
        for table in tables:
            columns = plan_columns(table, source_columns(my, table), target_columns(pg, table))
            types = [target_columns(pg, table)[c][0] for c in columns]
            left = source_fingerprint(my, table, columns, types)
            right = target_fingerprint(pg, table, columns, types)
            total += right[0]
            if left == right:
                log(f'{table:42} {right[0]:>8}  ok')
            else:
                failures.append(f'{table}: MySQL has {left[0]} rows, PostgreSQL {right[0]}' + ('' if left[0] != right[0] else ' (same count, different content)'))
                log(f'{table:42} {right[0]:>8}  MISMATCH')
        keys, orphans = orphan_checks(pg)
        failures += orphans
        log(f'foreign keys checked for orphan rows: {keys}, problems: {len(orphans)}')
        sequences, bad = sequence_problems(pg)
        failures += bad
        log(f'sequences checked: {sequences}, problems: {len(bad)}')
        log(f'{total} rows in {len(tables)} tables compared by count and content hash')
    my.close()
    for failure in failures:
        log('FAIL ' + failure)
    return failures


def run_inspect(log=print):
    my = connect_mysql()
    tables = source_tables(my)
    with my.cursor() as cursor:
        counts = {}
        for table in tables:
            cursor.execute(f'SELECT COUNT(*) FROM {quote_my(table)}')
            counts[table] = cursor.fetchone()[0]
        queued = {}
        for table in ('cap_api.published', 'cap_api.received', 'cap_worker.published', 'cap_worker.received'):
            try:
                cursor.execute(f'SELECT COUNT(*) FROM {quote_my(table)} WHERE StatusName <> %s', ('Succeeded',))
                queued[table] = cursor.fetchone()[0]
            except Exception:
                queued[table] = 'n/a'
        try:
            cursor.execute("SELECT COUNT(*) FROM `hangfire.Job` WHERE StateName IN ('Enqueued','Processing','Scheduled','Awaiting')")
            jobs = cursor.fetchone()[0]
        except Exception:
            jobs = 'n/a'
        in_flight = {}
        for label, query in (
                ('Runner operations not completed', 'SELECT COUNT(*) FROM `RunnerExecutionOperations` WHERE `CompletedAtUtc` IS NULL'),
                ('Action executions running', "SELECT COUNT(*) FROM `AppModuleExecutions` WHERE `Status` IN ('Dispatching','Running') AND `CompletedAtUtc` IS NULL")):
            try:
                cursor.execute(query)
                in_flight[label] = cursor.fetchone()[0]
            except Exception:
                in_flight[label] = 'n/a'
    my.close()
    log(f'{len(tables)} application tables, {sum(counts.values())} rows')
    for table, count in sorted(counts.items(), key=lambda item: -item[1])[:8]:
        log(f'  {table}: {count}')
    log('Not copied (recreated by the application): __EFMigrationsHistory, CAP messages, Hangfire jobs')
    for table, count in queued.items():
        log(f'  CAP {table} not succeeded: {count}')
    log(f'  Hangfire jobs waiting or running: {jobs}')
    for label, count in in_flight.items():
        log(f'  {label}: {count}')
    pending = [c for c in list(queued.values()) + [jobs] + list(in_flight.values()) if isinstance(c, int) and c > 0]
    return 1 if pending else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['inspect', 'copy', 'verify'])
    args = parser.parse_args(argv)
    try:
        if args.command == 'inspect':
            return run_inspect()
        if args.command == 'copy':
            run_copy()
            return 0
        return 1 if run_verify() else 0
    except MigrationError as error:
        print(f'Migration stopped: {error}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
