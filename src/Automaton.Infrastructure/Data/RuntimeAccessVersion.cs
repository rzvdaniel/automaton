namespace Automaton.Api.Data
{
    /// <summary>Counts changes to who may use an app, so a short-lived "this visitor may open the app" answer can be dropped at once.</summary>
    public static class RuntimeAccessVersion
    {
        private static long version;

        public static long Current => Interlocked.Read(ref version);

        public static void Bump() => Interlocked.Increment(ref version);
    }
}
