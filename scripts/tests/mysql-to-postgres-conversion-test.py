#!/usr/bin/env python3
"""Value conversion, table/column mapping and comparison rules of the MySQL to PostgreSQL data migration (no databases needed)."""
import datetime
import decimal
import importlib.util
from pathlib import Path
import unittest
import uuid

source = Path(__file__).resolve().parents[2] / 'docker/install/mysql_to_postgres.py'
spec = importlib.util.spec_from_file_location('mysql_to_postgres', source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

Error = module.MigrationError


class ConversionTests(unittest.TestCase):
    def convert(self, value, pg_type):
        return module.convert(value, pg_type, 'Users.Id')

    def test_null_is_kept_for_every_type(self):
        for pg_type in ('uuid', 'timestamp with time zone', 'boolean', 'jsonb', 'text', 'citext', 'integer', 'numeric', 'bytea'):
            self.assertIsNone(self.convert(None, pg_type))

    def test_guid_text_becomes_uuid(self):
        text = '3f2504e0-4f89-11d3-9a0c-0305e82c3301'
        self.assertEqual(self.convert(text, 'uuid'), uuid.UUID(text))
        with self.assertRaisesRegex(Error, 'Users.Id'):
            self.convert('not-a-guid', 'uuid')

    def test_datetime_is_read_as_utc_and_keeps_microseconds(self):
        value = datetime.datetime(2026, 11, 3, 10, 20, 30, 123456)
        converted = self.convert(value, 'timestamp with time zone')
        self.assertEqual(converted, datetime.datetime(2026, 11, 3, 10, 20, 30, 123456, tzinfo=datetime.timezone.utc))
        self.assertEqual(converted.utcoffset(), datetime.timedelta(0))

    def test_a_datetime_that_already_has_a_zone_is_refused(self):
        with self.assertRaisesRegex(Error, 'time zone'):
            self.convert(datetime.datetime(2026, 1, 1, tzinfo=datetime.timezone(datetime.timedelta(hours=2))), 'timestamp with time zone')

    def test_tinyint_becomes_boolean_and_other_values_are_refused(self):
        self.assertIs(self.convert(1, 'boolean'), True)
        self.assertIs(self.convert(0, 'boolean'), False)
        with self.assertRaisesRegex(Error, 'boolean'):
            self.convert(2, 'boolean')

    def test_json_must_be_valid_and_must_not_contain_a_null_character(self):
        self.assertEqual(self.convert('{"a": [1, 2]}', 'jsonb'), '{"a": [1, 2]}')
        with self.assertRaisesRegex(Error, 'JSON'):
            self.convert('{broken', 'jsonb')
        with self.assertRaisesRegex(Error, r'\\u0000|null character'):
            self.convert('{"a": "x\\u0000y"}', 'jsonb')

    def test_text_with_a_nul_character_is_refused(self):
        for pg_type in ('text', 'citext', 'character varying'):
            with self.assertRaisesRegex(Error, 'null character'):
                self.convert('a\x00b', pg_type)
        self.assertEqual(self.convert('ünïcode ✓', 'text'), 'ünïcode ✓')

    def test_numbers_keep_their_exact_value(self):
        self.assertEqual(self.convert(decimal.Decimal('12.50'), 'numeric'), decimal.Decimal('12.50'))
        self.assertEqual(self.convert(7, 'integer'), 7)
        self.assertEqual(self.convert(2 ** 40, 'bigint'), 2 ** 40)
        self.assertEqual(self.convert(1.5, 'real'), 1.5)

    def test_bytes_stay_bytes(self):
        self.assertEqual(self.convert(b'\x00\x01', 'bytea'), b'\x00\x01')

    def test_unknown_target_type_is_refused_not_guessed(self):
        with self.assertRaisesRegex(Error, 'tsvector'):
            self.convert('x', 'tsvector')


class TableAndColumnMappingTests(unittest.TestCase):
    def test_framework_and_queue_tables_are_not_copied(self):
        for name in ('__EFMigrationsHistory', 'cap_api.published', 'cap_worker.received', 'hangfire.Job', 'hangfire.AggregatedCounter'):
            self.assertTrue(module.is_excluded_source_table(name), name)
        for name in ('Users', 'Apps', 'RefreshTokens', 'AuditEvents'):
            self.assertFalse(module.is_excluded_source_table(name), name)

    def test_every_source_column_needs_a_target_column(self):
        with self.assertRaisesRegex(Error, 'Legacy'):
            module.plan_columns('Users', ['Id', 'Legacy'], {'Id': ('uuid', False, False)})

    def test_a_new_required_target_column_without_default_is_refused(self):
        target = {'Id': ('uuid', False, False), 'Added': ('integer', False, False)}
        with self.assertRaisesRegex(Error, 'Added'):
            module.plan_columns('Users', ['Id'], target)

    def test_new_optional_or_defaulted_target_columns_are_left_alone(self):
        target = {'Id': ('uuid', False, False), 'Note': ('text', True, False), 'Flag': ('boolean', False, True)}
        self.assertEqual(module.plan_columns('Users', ['Id'], target), ['Id'])


class ComparisonTests(unittest.TestCase):
    def test_equal_values_hash_the_same_across_representations(self):
        pairs = [
            ('3f2504e0-4f89-11d3-9a0c-0305e82c3301', uuid.UUID('3f2504e0-4f89-11d3-9a0c-0305e82c3301'), 'uuid'),
            (datetime.datetime(2026, 1, 2, 3, 4, 5, 600000), datetime.datetime(2026, 1, 2, 3, 4, 5, 600000, tzinfo=datetime.timezone.utc), 'timestamp with time zone'),
            (1, True, 'boolean'),
            ('{"b": 1,  "a": [true]}', '{"a": [true], "b": 1}', 'jsonb'),
            (decimal.Decimal('1.50'), decimal.Decimal('1.5'), 'numeric'),
        ]
        for left, right, pg_type in pairs:
            self.assertEqual(module.canonical(left, pg_type), module.canonical(right, pg_type), pg_type)

    def test_different_values_hash_differently(self):
        self.assertNotEqual(module.canonical('Abc', 'text'), module.canonical('abc', 'text'))
        self.assertNotEqual(module.canonical(0, 'boolean'), module.canonical(1, 'boolean'))
        self.assertNotEqual(module.canonical(None, 'text'), module.canonical('', 'text'))


if __name__ == '__main__':
    unittest.main()
