using Automaton.Core.Services;
using Serilog;
using System.Net.Http;
using System.Net.Sockets;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class HubConnectionFailureReporter(ConfigurationService configService, ILogger logger)
{    public void Report(Exception ex, bool isReconnecting, Action markReconnecting)
    {
        // Get current hub URL for error reporting
        var hubUrl = $"{configService.BaseUrl}/{configService.WorkflowHubUrl}";
        
        // Log full technical details with stack trace to file logs only
        logger.Error(ex, "Connection attempt failed. Server: {Server}, HubUrl: {HubUrl}, Error: {ErrorType}", 
            configService.BaseUrl, hubUrl, ex.GetType().Name);
        
        var friendlyMessage = GetFriendlyErrorMessage(ex);
        
        // Don't flood console with repeated circuit breaker messages
        if (ex.Message.Contains("circuit is now open"))
        {
            // Circuit breaker is open - just log once
            if (!isReconnecting)
            {
                WriteLine($"⏸️  Connection paused temporarily due to repeated failures");
                WriteLine($"   The runner will automatically retry after the cool-down period");
                markReconnecting(); // Use this flag to prevent repeated messages
            }
            return;
        }
        
        WriteLine($"❌ Connection failed: {friendlyMessage}");
        WriteLine();
        
        // Provide specific troubleshooting guidance
        switch (ex)
        {
            case HttpRequestException httpEx when httpEx.Message.Contains("refused"):
            case SocketException sockEx when sockEx.SocketErrorCode == SocketError.ConnectionRefused:
                WriteLine("🔍 Connection refused - this usually means:");
                WriteLine("   • The Automaton server is not running");
                WriteLine("   • The server URL is incorrect");
                WriteLine("   • A firewall is blocking the connection");
                WriteLine();
                WriteLine("💡 Troubleshooting steps:");
                WriteLine($"   • Check if the server is running at: {configService.BaseUrl}");
                WriteLine("   • Verify the server URL is correct");
                WriteLine("   • Check network connectivity and firewall settings");
                break;
                
            case HttpRequestException httpEx when httpEx.Message.Contains("timeout"):
            case TaskCanceledException when ex.Message.Contains("timeout"):
                WriteLine("🔍 Connection timeout - this usually means:");
                WriteLine("   • The server is slow to respond");
                WriteLine("   • Network connectivity issues");
                WriteLine("   • Server is under heavy load");
                WriteLine();
                WriteLine("💡 The runner will automatically retry connecting...");
                break;
                
            case HttpRequestException httpEx when httpEx.Message.Contains("404") || httpEx.Message.Contains("Not Found"):
                WriteLine("🔍 Hub endpoint not found - this usually means:");
                WriteLine("   • The SignalR hub is not enabled on the server");
                WriteLine("   • The server URL path is incorrect");
                WriteLine($"   • Expected hub path: {hubUrl}");
                break;
                
            case HttpRequestException httpEx when httpEx.Message.Contains("401") || httpEx.Message.Contains("Unauthorized"):
                WriteLine("🔍 Authentication failed - this usually means:");
                WriteLine("   • The API key is invalid or expired");
                WriteLine("   • The API key doesn't have the required permissions");
                WriteLine();
                WriteLine("💡 Try running: automaton-runner auth --api-key your_key_here");
                break;
                
            default:
                WriteLine("💡 General troubleshooting steps:");
                WriteLine("   • Verify the server is running and accessible");
                WriteLine("   • Check your API key and authentication");
                WriteLine("   • Confirm network connectivity");
                WriteLine($"   • Test server connectivity: {configService.BaseUrl}");
                break;
        }
        
        WriteLine();
        WriteLine("🔄 The runner will continue trying to connect automatically...");
        WriteLine("   Press 'q' to quit or wait for connection to be established.");
    }

    public static string GetFriendlyErrorMessage(Exception? ex)
    {
        return ex switch
        {
            null => "Unknown error",
            HttpRequestException httpEx when httpEx.Message.Contains("refused") => "Server connection refused",
            SocketException sockEx when sockEx.SocketErrorCode == SocketError.ConnectionRefused => "Connection refused by server",
            HttpRequestException httpEx when httpEx.Message.Contains("timeout") => "Connection timeout", 
            TaskCanceledException when ex.Message.Contains("timeout") => "Connection timeout",
            HttpRequestException httpEx when httpEx.Message.Contains("404") => "Hub endpoint not found",
            HttpRequestException httpEx when httpEx.Message.Contains("401") => "Authentication failed",
            _ => ex.Message
        };
    }

}
