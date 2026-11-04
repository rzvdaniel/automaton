namespace Automaton.Cli.Services;

internal static class PublishedCertificate
{
    internal static async Task<bool> WaitAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url.Scheme != Uri.UriSchemeHttps) return true;

        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return await WaitAsync(client, url, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2), cancellationToken);
    }

    internal static async Task<bool> WaitAsync(
        HttpClient client, Uri url, TimeSpan timeout, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                try
                {
                    // Any response from the original HTTPS endpoint establishes that TLS validated.
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    return true;
                }
                catch (HttpRequestException)
                {
                    // The route or its certificate can take time to become available.
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                {
                    // Retry a per-request timeout within the overall deadline.
                }

                await Task.Delay(retryDelay, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
