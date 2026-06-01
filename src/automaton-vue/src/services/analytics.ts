import posthog from 'posthog-js'

const posthogKey = import.meta.env.VITE_POSTHOG_KEY?.trim() || ''
const posthogHost = import.meta.env.VITE_POSTHOG_HOST?.trim() || ''
const isAnalyticsEnabled =
  !import.meta.env.DEV &&
  import.meta.env.VITE_POSTHOG_ENABLED === 'true' &&
  !!posthogKey &&
  !!posthogHost

let hasInitialized = false

function safeCall(action: () => void) {
  try {
    action()
  } catch (error) {
    console.warn('[analytics] PostHog call failed', error)
  }
}

function init() {
  if (!isAnalyticsEnabled || hasInitialized || typeof window === 'undefined') {
    return
  }

  safeCall(() => {
    posthog.init(posthogKey, {
      api_host: posthogHost,
      capture_pageview: 'history_change',
      autocapture: false,
      capture_pageleave: 'if_capture_pageview',
      persistence: 'localStorage+cookie',
      loaded: () => {
        hasInitialized = true
      },
    })
    hasInitialized = true
  })
}

export const analytics = {
  init,
}