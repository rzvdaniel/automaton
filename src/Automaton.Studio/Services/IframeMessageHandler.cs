using Microsoft.JSInterop;

namespace Automaton.Studio.Services;

/// <summary>
/// C#-based iframe message handler using global automatonMessageHandler.js
/// </summary>
public class IframeMessageHandler : IAsyncDisposable
{
    private readonly IJSRuntime jsRuntime;
    private readonly IframeProxyService proxyService;
    private readonly ILogger logger;
    private DotNetObjectReference<IframeMessageHandler>? dotNetRef;

    public IframeMessageHandler(
        IJSRuntime jsRuntime,
        IframeProxyService proxyService)
    {
        this.jsRuntime = jsRuntime;
        this.proxyService = proxyService;
        this.logger = Log.ForContext<IframeMessageHandler>();
    }

    /// <summary>
    /// Initialize the message listener using global automatonMessageHandler
    /// </summary>
    public async Task InitializeAsync()
    {
        logger.Information("[IframeMessageHandler] Initializing message handler");

        // Check if automatonMessageHandler exists
        try
        {
            var exists = await jsRuntime.InvokeAsync<bool>("eval", "typeof window.automatonMessageHandler !== 'undefined'");
            logger.Information("[IframeMessageHandler] automatonMessageHandler exists: {Exists}", exists);

            if (!exists)
            {
                logger.Warning("[IframeMessageHandler] automatonMessageHandler.js not present - attempting to load dynamically");

                // Dynamically inject the script tag into the host page so it's available
                try
                {
                    // Insert script element to load automatonMessageHandler.js from web root
                    var injectScript = @"(function(){
                        if (typeof window.automatonMessageHandler !== 'undefined') return true;
                        var s = document.createElement('script');
                        s.src = '/automatonMessageHandler.js';
                        s.async = true;
                        document.head.appendChild(s);
                        return true;
                    })();";

                    await jsRuntime.InvokeAsync<object>("eval", injectScript);

                    // Wait a short, fixed delay for the script to load
                    await Task.Delay(300);
                    exists = await jsRuntime.InvokeAsync<bool>("eval", "typeof window.automatonMessageHandler !== 'undefined'");
                    if (!exists)
                    {
                        logger.Error("[IframeMessageHandler] Failed to load automatonMessageHandler.js dynamically after delay");
                        return;
                    }
                    else
                    {
                        logger.Information("[IframeMessageHandler] automatonMessageHandler loaded dynamically after delay");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "[IframeMessageHandler] Error injecting automatonMessageHandler.js dynamically");
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[IframeMessageHandler] Error checking for automatonMessageHandler");
            throw;
        }

        // Create DotNet reference for callbacks
        dotNetRef = DotNetObjectReference.Create(this);

        // Call global JavaScript function
        await jsRuntime.InvokeVoidAsync("automatonMessageHandler.init", dotNetRef);

        logger.Information("[IframeMessageHandler] Initialized - ready to receive messages");
    }

    /// <summary>
    /// Handle incoming messages from iframe (called from JavaScript)
    /// </summary>
    [JSInvokable("HandleMessage")]
    public async Task<string> HandleMessage(string messageJson)
    {
        logger.Information("[IframeMessageHandler] ===== MESSAGE RECEIVED IN C# =====");
        logger.Information("[IframeMessageHandler] Raw message: {Message}", messageJson);

        try
        {
            // Parse the message with case-insensitive property names
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var message = System.Text.Json.JsonSerializer.Deserialize<MessageData>(messageJson, options);

            if (message?.Type != "apiRequest" || message.Payload == null)
            {
                logger.Warning("[IframeMessageHandler] Invalid message type or missing payload. Type: {Type}, Payload null: {PayloadNull}", 
                    message?.Type, message?.Payload == null);
                return CreateErrorResponse(null, "Invalid message format");
            }

            logger.Information("[IframeMessageHandler] Processing API request: {RequestId} - {Path}", 
                message.Payload.Id, message.Payload.Path);

            // Forward to proxy service (this is where you should see the next log/breakpoint)
            logger.Information("[IframeMessageHandler] Calling IframeProxyService.HandleApiRequest...");
            var result = await proxyService.HandleApiRequest(message.Payload);
            logger.Information("[IframeMessageHandler] IframeProxyService completed");

            // Create response
            var response = new
            {
                type = "apiResponse",
                id = message.Payload.Id,
                result = result
            };

            var responseJson = System.Text.Json.JsonSerializer.Serialize(response);
            logger.Information("[IframeMessageHandler] Returning response to JavaScript");

            return responseJson;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[IframeMessageHandler] ERROR in HandleMessage");
            return CreateErrorResponse(null, $"Error: {ex.Message}");
        }
    }

    private string CreateErrorResponse(string? requestId, string errorMessage)
    {
        var response = new
        {
            type = "apiResponse",
            id = requestId ?? "unknown",
            result = new
            {
                error = "Handler error",
                message = errorMessage,
                success = false
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(response);
    }

    public async ValueTask DisposeAsync()
    {
        logger.Information("[IframeMessageHandler] Disposing");

        if (dotNetRef != null)
        {
            try
            {
                var exists = await jsRuntime.InvokeAsync<bool>("eval", "typeof window.automatonMessageHandler !== 'undefined'");
                if (exists)
                {
                    await jsRuntime.InvokeVoidAsync("automatonMessageHandler.dispose");
                }
            }
            catch (Exception ex)
            {
                // Ignore JS errors during dispose but log for diagnostics
                logger.Warning(ex, "[IframeMessageHandler] Error while disposing JavaScript handler");
            }

            dotNetRef.Dispose();
        }
    }

    // Helper classes for deserialization
    private class MessageData
    {
        public string? Type { get; set; }
        public ProxyPayload? Payload { get; set; }
    }
}
