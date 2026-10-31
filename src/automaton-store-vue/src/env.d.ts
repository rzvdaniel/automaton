/// <reference types="vite/client" />

interface AutomatonStoreRuntimeConfig {
  apiBaseUrl?: string
  publicAppsBaseUrl?: string
  studioUrl?: string
  storeTitle?: string
  logoUrl?: string
}

// eslint-disable-next-line no-var
declare var __AUTOMATON_CONFIG__: AutomatonStoreRuntimeConfig | undefined
