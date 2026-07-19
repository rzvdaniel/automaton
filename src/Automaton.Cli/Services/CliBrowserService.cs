namespace Automaton.Cli.Services;

public static class CliBrowserService
{
    public static bool TryOpenUrl(string url)
    {
        try
        {
            OpenUrl(url);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL is required.", nameof(url));
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };

        System.Diagnostics.Process.Start(startInfo);
    }
}
