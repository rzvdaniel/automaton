/**
 * Automaton SDK for Web Apps
 * Provides utilities for web apps to interact with Automaton flows
 */
window.Automaton = window.Automaton || {
    appId: null, // Set by WebAppViewer when loading the app
    
    /**
     * Execute a flow from a web app
     * @param {string} flowId - The GUID of the flow to execute
     * @param {object} inputs - Optional input variables for the flow
     * @param {string[]} runnerNames - Optional array of runner names to execute on
* @returns {Promise<object>} Flow execution result
     */
 executeFlow: async function(flowId, inputs = {}, runnerNames = null) {
 if (!this.appId) {
       throw new Error('Automaton SDK not initialized - appId is missing');
        }

        if (!flowId) {
            throw new Error('flowId is required');
        }

      try {
         const requestBody = {
   inputs: inputs || {}
};

        if (runnerNames && Array.isArray(runnerNames) && runnerNames.length > 0) {
      requestBody.runnerNames = runnerNames;
          }

     console.log(`[Automaton SDK] Executing flow ${flowId} for app ${this.appId}`);

  const response = await fetch(`/api/web-apps/${this.appId}/flows/${flowId}/execute`, {
        method: 'POST',
      headers: {
  'Content-Type': 'application/json'
       },
       body: JSON.stringify(requestBody)
       });

  if (!response.ok) {
 let errorMessage = `HTTP ${response.status}`;
              try {
             const error = await response.json();
  errorMessage = error.message || errorMessage;
        } catch (e) {
             // If we can't parse the error, use the status text
           errorMessage = response.statusText || errorMessage;
       }
       throw new Error(errorMessage);
      }

   const result = await response.json();
    console.log('[Automaton SDK] Flow execution result:', result);
          return result;

        } catch (error) {
            console.error('[Automaton SDK] Error executing flow:', error);
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
};

console.log('[Automaton SDK] Loaded successfully');
