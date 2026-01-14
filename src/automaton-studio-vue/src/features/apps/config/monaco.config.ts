/**
 * Monaco Editor Configuration
 * Must be imported before monaco-editor to prevent worker errors
 */

// Create a stub worker that does nothing
class StubWorker {
  postMessage() {
    // No-op: do nothing
  }
  addEventListener() {
    // No-op: do nothing
  }
  removeEventListener() {
    // No-op: do nothing
  }
  terminate() {
    // No-op: do nothing
  }
  dispatchEvent() {
    // No-op: do nothing
    return true
  }
}

// Configure Monaco environment globally to use stub workers
// This prevents "Cannot read properties of undefined (reading 'postMessage')" errors
if (typeof window !== 'undefined') {
  (window as any).MonacoEnvironment = {
    getWorker(_workerId: string, _label: string) {
      // Return a stub worker that implements the Worker interface
      // but doesn't actually do anything
      return new StubWorker() as any
    },
  }
}

export {}
