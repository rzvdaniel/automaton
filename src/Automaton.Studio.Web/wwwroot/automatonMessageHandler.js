/**
 * Blazor-friendly iframe message handler
 * Simple global listener that calls back to Blazor C#
 */

console.log('[MessageHandler] Script loading...');

window.automatonMessageHandler = {
    dotnetHelper: null,

    init: function (helper) {
        this.dotnetHelper = helper;
        console.log('[MessageHandler] init() called with helper:', helper);
        
        window.addEventListener('message', async (e) => {
            console.log('[MessageHandler] Message event:', e.origin, e.data?.type);
            
            // Accept 'null' origin (srcdoc) or same origin
            if (e.origin !== 'null' && e.origin !== window.origin) {
                console.warn('[MessageHandler] REJECTED - wrong origin');
                return;
            }

            const { type, payload } = e.data || {};
            if (type !== 'apiRequest') {
                console.log('[MessageHandler] IGNORED - not apiRequest');
                return;
            }

            console.log('[MessageHandler] ? API REQUEST! ID:', payload?.id);

            try {
                // Call C# method
                const messageJson = JSON.stringify(e.data);
                console.log('[MessageHandler] Calling C# HandleMessage...');
                const result = await this.dotnetHelper.invokeMethodAsync('HandleMessage', messageJson);
                console.log('[MessageHandler] ? C# returned:', result);
                
                // Send response back
                const response = JSON.parse(result);
                if (e.source) {
                    e.source.postMessage(response, '*');
                    console.log('[MessageHandler] ? Response sent to iframe');
                }
            } catch (err) {
                console.error('[MessageHandler] ? ERROR:', err);
                
                // Send error response
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
        
        console.log('[MessageHandler] ? Event listener registered!');
    },

    dispose: function () {
        console.log('[MessageHandler] dispose() called');
        this.dotnetHelper = null;
    }
};

console.log('[MessageHandler] ? Script loaded! Object available:', !!window.automatonMessageHandler);
