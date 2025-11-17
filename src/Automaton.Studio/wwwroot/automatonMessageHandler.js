/**
 * Blazor-friendly iframe message handler
 * Secure postMessage listener that calls back to Blazor C#
 * 
 * SECURITY: Only accepts messages from same-origin iframes (srcdoc has 'null' origin)
 */

console.log('[MessageHandler] Script loading...');

window.automatonMessageHandler = {
    dotnetHelper: null,

    init: function (helper) {
        this.dotnetHelper = helper;
        console.log('[MessageHandler] init() called');
        
        window.addEventListener('message', async (e) => {
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
                
                if (e.source && payload) {
                    e.source.postMessage({
                        type: 'apiResponse',
                        id: payload.id,
                        result: { 
                            error: 'Handler error', 
                            message: err.toString(), 
                            success: false 
                        }
                    }, '*');
                }
            }
        });
        
        console.log('[MessageHandler] Event listener registered');
    },

    dispose: function () {
        console.log('[MessageHandler] dispose() called');
        this.dotnetHelper = null;
    }
};

console.log('[MessageHandler] Script loaded');
