using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog.Sinks.Http;
using System.IO;
using System.Net.Http.Json;

namespace Automaton.Core.Logs
{
    public class SerilogHttpClient : IHttpClient
    {
        private readonly HttpClient httpClient;

        public SerilogHttpClient(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public void Configure(IConfiguration configuration)
        {
            // Does nothing for now
        }

        public void Dispose()
        {
            // Do not dispose HttpClient - it's managed by HttpClientFactory via DI
        }

        public async Task<HttpResponseMessage> PostAsync(string requestUri, Stream stream, CancellationToken cancellationToken)
        {
            var reader = new StreamReader(stream);
            var logEventsText = reader.ReadToEnd();
            var logEvents = JsonConvert.DeserializeObject<IEnumerable<SerilogHttpLogEvent>>(logEventsText);

            var response = await httpClient.PostAsJsonAsync(requestUri, logEvents, cancellationToken);

            return response;
        }
    }
}
