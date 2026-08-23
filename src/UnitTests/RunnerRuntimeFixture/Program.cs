using System.Net;
using System.Net.Sockets;
using System.Text;

var port = int.Parse(Environment.GetEnvironmentVariable("AUTOMATON_PORT")!);
var http = args.Contains("--http", StringComparer.Ordinal);
Console.WriteLine($"stdout:{string.Join('|', args)}:{Environment.GetEnvironmentVariable("TEST_RUNTIME_VALUE")}");
Console.Error.WriteLine("stderr:fixture");
Console.Out.Flush();
Console.Error.Flush();

var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
try
{
    while (true)
    {
        using var client = await listener.AcceptTcpClientAsync();
        if (!http)
            continue;

        using var stream = client.GetStream();
        var buffer = new byte[1024];
        _ = await stream.ReadAsync(buffer);
        var body = Encoding.UTF8.GetBytes("ready");
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
        await stream.WriteAsync(body);
    }
}
finally
{
    listener.Stop();
}
