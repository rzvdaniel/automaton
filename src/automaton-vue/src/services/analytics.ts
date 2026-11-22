// Cookie-free page counts with our self-hosted GoatCounter (stats.apps.automaton.monster).
// Off in dev and when VITE_GOATCOUNTER_URL is empty.
const countUrl = import.meta.env.VITE_GOATCOUNTER_URL?.trim() || ''
const isEnabled = !import.meta.env.DEV && !!countUrl

type GoatCounter = { count?: (vars: { path: string }) => void; no_onload?: boolean }
declare global {
  interface Window {
    goatcounter?: GoatCounter
  }
}

let hasInitialized = false

function init() {
  if (!isEnabled || hasInitialized || typeof window === 'undefined') return
  hasInitialized = true
  // Route changes are counted by pageview(), so count.js must not count on load as well.
  window.goatcounter = { no_onload: true }
  const script = document.createElement('script')
  script.async = true
  script.src = countUrl.replace(/\/count$/, '/count.js')
  script.dataset.goatcounter = countUrl
  script.onload = () => pageview(window.location.pathname)
  document.head.appendChild(script)
}

function pageview(path: string) {
  if (!isEnabled) return
  try {
    window.goatcounter?.count?.({ path })
  } catch (error) {
    console.warn('[analytics] GoatCounter call failed', error)
  }
}

export const analytics = {
  init,
  pageview,
}
