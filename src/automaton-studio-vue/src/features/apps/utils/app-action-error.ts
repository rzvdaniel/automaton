/** How long Resume, Restart and Pause wait: the API waits up to ~150 s for the Runner, so Studio waits longer. */
export const APP_ACTION_TIMEOUT_MS = 180_000

export const APP_START_PROGRESS = 'Starting the app… this can take up to 2–3 minutes.'

/** The API's own message (with the app's reason) when there is one; a clear text for a client timeout. */
export function appActionErrorMessage(failure: unknown, fallback: string): string {
  const error = failure as { code?: string; response?: { data?: { message?: string } } } | null
  const message = error?.response?.data?.message
  if (message) return message
  if (error?.code === 'ECONNABORTED' || error?.code === 'ETIMEDOUT') {
    return 'Studio stopped waiting for the app after 3 minutes. It may still be starting; reload the page to see its state.'
  }
  return fallback
}
