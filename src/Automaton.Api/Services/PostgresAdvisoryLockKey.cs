using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Services;

/// <summary>Maps a lock name to a PostgreSQL advisory lock key and checks that a session still holds it.</summary>
internal static class PostgresAdvisoryLockKey
{
    // pg_locks stores a bigint advisory key as two oids: its high and low 32 bits.
    internal const string HeldBySessionSql = """
        SELECT EXISTS (
            SELECT 1 FROM pg_locks
            WHERE locktype = 'advisory' AND granted AND pid = pg_backend_pid() AND objsubid = 1
              AND classid::bigint = @classid AND objid::bigint = @objid)
        """;

    internal static long From(string name)
        => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(name)), 0);

    internal static long ClassId(long key) => (long)((ulong)key >> 32);

    internal static long ObjectId(long key) => (long)((ulong)key & 0xFFFFFFFFUL);
}
