/**
 * Blazor-friendly iframe message handler
 * Secure postMessage listener that calls back to Blazor C#
 * 
 * SECURITY: Only accepts messages from same-origin iframes (srcdoc has 'null' origin)
 */

console.log('[MessageHandler] Script loading...');

window.automatonMessageHandler = {
    dotnetHelper: null,
    messageHandler: null,  // Store the handler function so we can remove it

    init: function (helper) {
        console.log('[MessageHandler] init() called');
        
        // Remove any existing listener before adding a new one
        // This prevents duplicate handlers when the component is re-rendered
        if (this.messageHandler) {
            window.removeEventListener('message', this.messageHandler);
            console.log('[MessageHandler] Removed previous event listener');
        }
        
        // Store the new dotnet helper (replaces any stale reference)
        this.dotnetHelper = helper;
        
        // Create the message handler function
        this.messageHandler = async (e) => {
            // SECURITY: Only accept messages from same origin or srcdoc iframes
            // srcdoc iframes have origin 'null' which is safe because they inherit parent's security context
            const isSameOrigin = e.origin === window.origin;
            const isSrcdocIframe = e.origin === 'null';
            
            if (!isSameOrigin && !isSrcdocIframe) {
                console.warn('[MessageHandler] SECURITY: Rejected message from foreign origin:', e.origin);
                return;
            }

            // SECURITY: Verify message is from an iframe, not external window
            if (e.source === window || e.source === window.parent) {
                console.warn('[MessageHandler] SECURITY: Rejected message from parent/self');
                return;
            }

            const { type, payload } = e.data || {};

            // Handle ping/pong for initialization handshake
            if (type === 'ping' && e.source) {
                console.log('[MessageHandler] Ping received, sending pong');
                e.source.postMessage({ type: 'pong' }, '*');
                return;
            }

            // Only process API requests
            if (type !== 'apiRequest') {
                return;
            }

            console.log('[MessageHandler] API REQUEST ID:', payload?.id);

            // SECURITY: Validate payload structure before processing
            if (!payload || !payload.id || !payload.path) {
                console.error('[MessageHandler] SECURITY: Invalid payload structure');
                if (e.source) {
                    e.source.postMessage({
                        type: 'apiResponse',
                        id: payload?.id || 'unknown',
                        result: { 
                            error: 'Invalid request', 
                            message: 'Missing required fields', 
                            success: false 
                        }
                    }, '*');
                }
                return;
            }

            // Check if we have a valid dotnet helper
            if (!this.dotnetHelper) {
                console.error('[MessageHandler] No dotnet helper available - handler may have been disposed');
                if (e.source) {
                    e.source.postMessage({
                        type: 'apiResponse',
                        id: payload.id,
                        result: { 
                            error: 'Handler not ready', 
                            message: 'Please refresh the page and try again', 
                            success: false 
                        }
                    }, '*');
                }
                return;
            }

            // SECURITY: Path validation happens in C# (IframeProxyService)
            // Only /api/web-apps/* endpoints are allowed
            try {
                const messageJson = JSON.stringify(e.data);
                const result = await this.dotnetHelper.invokeMethodAsync('HandleMessage', messageJson);
                
                const response = JSON.parse(result);
                if (e.source) {
                    e.source.postMessage(response, '*');
                    console.log('[MessageHandler] Response sent to iframe');
                }
            } catch (err) {
                console.error('[MessageHandler] ERROR:', err);
                
                // Check if the error is due to disposed object reference
                const isDisposedError = err.message && (
                    err.message.includes('disposed') || 
                    err.message.includes('No .NET object')
                );
                
                if (e.source && payload) {
                    e.source.postMessage({
                        type: 'apiResponse',
                        id: payload.id,
                        result: { 
                            error: isDisposedError ? 'Handler expired' : 'Handler error', 
                            message: isDisposedError ? 'Please refresh the page and try again' : err.toString(), 
                            success: false 
                        }
                    }, '*');
                }
            }
        };
        
        // Bind the handler to this context so 'this' works correctly
        this.messageHandler = this.messageHandler.bind(this);
        
        window.addEventListener('message', this.messageHandler);
        
        console.log('[MessageHandler] Event listener registered');
    },

    dispose: function () {
        console.log('[MessageHandler] dispose() called');
        
        // Remove the event listener
        if (this.messageHandler) {
            window.removeEventListener('message', this.messageHandler);
            this.messageHandler = null;
            console.log('[MessageHandler] Event listener removed');
        }
        
        this.dotnetHelper = null;
    }
};

console.log('[MessageHandler] Script loaded');
