/**
 * Automaton SDK for Web Apps
 * Provides utilities for web apps to interact with Automaton flows
 */

// Log initial state
console.log('[Automaton SDK] Starting initialization...');
console.log('[Automaton SDK] Current window.Automaton state:', window.Automaton);

window.Automaton = window.Automaton || {};

// Log after creating/preserving object
console.log('[Automaton SDK] After object creation, window.Automaton:', window.Automaton);
console.log('[Automaton SDK] appSlug before preservation:', window.Automaton.appSlug);

// Preserve appSlug if already set by WebAppViewer, otherwise null
window.Automaton.appSlug = window.Automaton.appSlug || null;

console.log('[Automaton SDK] appSlug after preservation:', window.Automaton.appSlug);
console.log('[Automaton SDK] appSlug type:', typeof window.Automaton.appSlug);

// Define SDK methods
Object.assign(window.Automaton, {
    /**
     * Execute a flow from a web app
     * @param {string} flowIdentifier - Flow name (e.g., 'Get-Articles') or GUID
     * @param {object} inputs - Optional input variables for the flow
     * @param {string[]} runnerNames - Optional array of runner names to execute on
     * @returns {Promise<object>} Flow execution result
     */
    executeFlow: async function(flowIdentifier, inputs = {}, runnerNames = null) {
        console.log('[Automaton SDK] executeFlow called');
        console.log('[Automaton SDK] - flowIdentifier:', flowIdentifier);
        console.log('[Automaton SDK] - this.appSlug:', this.appSlug);
        console.log('[Automaton SDK] - window.Automaton.appSlug:', window.Automaton.appSlug);
   
     if (!this.appSlug) {
     console.error('[Automaton SDK] ERROR: appSlug is missing!');
 console.error('[Automaton SDK] Current state:', {
        'this.appSlug': this.appSlug,
       'window.Automaton.appSlug': window.Automaton.appSlug,
 'typeof appSlug': typeof this.appSlug,
                'window.Automaton keys': Object.keys(window.Automaton)
     });
       throw new Error('Automaton SDK not initialized - appSlug is missing');
        }

        if (!flowIdentifier) {
            console.error('[Automaton SDK] ERROR: flowIdentifier is required');
 throw new Error('flowIdentifier is required');
     }

        try {
  const requestBody = {
      inputs: inputs || {}
        };

       if (runnerNames && Array.isArray(runnerNames) && runnerNames.length > 0) {
    requestBody.runnerNames = runnerNames;
}

        const url = `/api/web-apps/${this.appSlug}/flows/${flowIdentifier}/execute`;
     console.log(`[Automaton SDK] Executing flow '${flowIdentifier}' for app '${this.appSlug}'`);
       console.log(`[Automaton SDK] Request URL: ${url}`);
   console.log(`[Automaton SDK] Request body:`, requestBody);

            const response = await fetch(url, {
    method: 'POST',
     headers: {
  'Content-Type': 'application/json'
         },
 body: JSON.stringify(requestBody)
});

     console.log(`[Automaton SDK] Response status: ${response.status}`);

   if (!response.ok) {
                let errorMessage = `HTTP ${response.status}`;
    try {
                    const error = await response.json();
        errorMessage = error.message || errorMessage;
       console.error('[Automaton SDK] Server error:', error);
     } catch (e) {
        // If we can't parse the error, use the status text
            errorMessage = response.statusText || errorMessage;
      console.error('[Automaton SDK] Could not parse error response');
       }
          throw new Error(errorMessage);
            }

      const result = await response.json();
            console.log('[Automaton SDK] Flow execution result:', result);
            return result;

        } catch (error) {
       console.error('[Automaton SDK] Error executing flow:', error);
    console.error('[Automaton SDK] Error stack:', error.stack);
     throw error;
        }
    },

    /**
     * Get a specific output variable from a flow execution result
     * @param {object} result - The flow execution result
     * @param {string} variableName - The name of the output variable
     * @returns {any} The value of the output variable, or undefined if not found
 */
    getOutputVariable: function(result, variableName) {
        if (!result || !result.outputVariables) {
            return undefined;
  }
        return result.outputVariables[variableName];
    },

    /**
     * Check if a flow execution was successful
     * @param {object} result - The flow execution result
  * @returns {boolean} True if the execution was successful
     */
    isSuccess: function(result) {
        return result && result.success === true;
    },

    /**
     * Get logs from a flow execution result
     * @param {object} result - The flow execution result
     * @returns {string[]} Array of log messages
 */
    getLogs: function(result) {
 return result && result.logs ? result.logs : [];
    }
});

console.log('[Automaton SDK] Methods assigned');
console.log('[Automaton SDK] Final window.Automaton object:', window.Automaton);
console.log('[Automaton SDK] Final appSlug value:', window.Automaton.appSlug);
console.log('[Automaton SDK] executeFlow function exists:', typeof window.Automaton.executeFlow === 'function');

console.log('[Automaton SDK] Loaded successfully');
if (window.Automaton.appSlug) {
    console.log('[Automaton SDK] ? App Slug is set:', window.Automaton.appSlug);
} else {
    console.warn('[Automaton SDK] ?? Warning: appSlug not set yet - should be set by WebAppViewer');
    console.warn('[Automaton SDK] ?? Current appSlug value:', window.Automaton.appSlug);
    console.warn('[Automaton SDK] ?? Type:', typeof window.Automaton.appSlug);
}

// Add global diagnostics function
window.Automaton.diagnostics = function() {
    console.group('[Automaton SDK] Diagnostics');
    console.log('appSlug:', window.Automaton.appSlug);
    console.log('appSlug type:', typeof window.Automaton.appSlug);
    console.log('appSlug === null:', window.Automaton.appSlug === null);
    console.log('appSlug === undefined:', window.Automaton.appSlug === undefined);
    console.log('appSlug === "":', window.Automaton.appSlug === '');
    console.log('Available properties:', Object.keys(window.Automaton));
    console.log('executeFlow exists:', typeof window.Automaton.executeFlow === 'function');
    console.log('Full object:', window.Automaton);
    console.groupEnd();
};

console.log('[Automaton SDK] ?? Run window.Automaton.diagnostics() to check SDK state');
