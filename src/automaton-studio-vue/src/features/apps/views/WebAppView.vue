<script setup lang="ts">
/**
 * WebAppView.vue
 * Serves published web apps at /web/{slug}
 * Fetches app HTML from backend and renders in fullscreen iframe
 */
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ProgressSpinner from 'primevue/progressspinner'
import Button from 'primevue/button'
import apiClient from '@/shared/services/api/axios-instance'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import rawPublishedScript from '../scripts/automaton-api-published.js?raw'
import { jsStr } from '../utils/js-str'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const isLoading = ref(true)
const error = ref<string | null>(null)
const htmlContent = ref<string | null>(null)
const appInfo = ref<{
  id: string
  name: string
  summary?: string
  requiresAuthentication: boolean
  isUserAuthenticated: boolean
} | null>(null)

const iframeRef = ref<HTMLIFrameElement>()

const slug = computed(() => route.params.slug as string)

function trimTrailingSlash(value: string): string {
  return value.replace(/\/+$/, '')
}

function getPublishedAppBaseUrls(responseUrl?: string): { apiBaseUrl: string; filesBaseUrl: string } {
  const fallbackOrigin = window.location.origin
  const publishedResponseUrl = responseUrl ? new URL(responseUrl, fallbackOrigin) : null
  const configuredBaseUrl = apiClient.defaults.baseURL
    ? new URL(String(apiClient.defaults.baseURL), fallbackOrigin)
    : null

  const apiBaseUrl = trimTrailingSlash(
    publishedResponseUrl?.origin ?? configuredBaseUrl?.origin ?? fallbackOrigin,
  )

  return {
    apiBaseUrl,
    filesBaseUrl: `${apiBaseUrl}/api/web/${encodeURIComponent(slug.value)}/files/`,
  }
}

// Check if user needs to authenticate
const needsAuth = computed(() => {
  return appInfo.value?.requiresAuthentication && !appInfo.value?.isUserAuthenticated
})

onMounted(async () => {
  await loadApp()
})

async function loadApp() {
  isLoading.value = true
  error.value = null

  try {
    const response = await apiClient.get(`/api/web/${slug.value}`)
    const { data } = response

    appInfo.value = {
      id: data.id,
      name: data.name,
      summary: data.summary,
      requiresAuthentication: data.requiresAuthentication,
      isUserAuthenticated: data.isUserAuthenticated
    }

    // Check if authentication is required
    if (data.requiresAuthentication && !data.isUserAuthenticated) {
      error.value = 'auth_required'
      return
    }

    // Inject automatonApi for published apps
    const { apiBaseUrl, filesBaseUrl } = getPublishedAppBaseUrls(response.request?.responseURL)
    // Pass the current auth token so authenticated apps can call protected API endpoints.
    // Anonymous apps use credentials:include (cookie fallback) — represented as empty string.
    const authToken = authStore.accessToken
    const publishedScript = `<script>\n${
      rawPublishedScript
        .replace(/__APP_SLUG__/g, jsStr(slug.value))
        .replace(/__BASE_URL__/g, jsStr(apiBaseUrl))
        .replace(/__AUTH_TOKEN__/g, jsStr(authToken ?? ''))
    }\n<\/script>`

    // Get HTML content
    let html = data.htmlContent || getDefaultHtml()

    // Strip any existing automatonApi injection to prevent double-injection
    html = html.replace(/<script\b[^>]*>[\s\S]*?\(function\s*\(\s*window\s*,\s*APP_(?:ID|SLUG)\b[\s\S]*?<\/script>/gi, '')

    // Resolve all relative file references through the published file endpoint so
    // the viewer uses the same asset-serving behavior as the public app itself.
    const baseTag = `<base href="${filesBaseUrl}">`

    // Inject script and base tag into HTML
    if (html.includes('<head>')) {
      const headIndex = html.indexOf('<head>') + 6
      html = html.slice(0, headIndex) + baseTag + publishedScript + html.slice(headIndex)
    } else if (html.includes('<body')) {
      const bodyMatch = html.match(/<body[^>]*>/)
      if (bodyMatch) {
        const bodyIndex = html.indexOf(bodyMatch[0]) + bodyMatch[0].length
        html = html.slice(0, bodyIndex) + baseTag + publishedScript + html.slice(bodyIndex)
      }
    } else {
      html = baseTag + publishedScript + html
    }

    htmlContent.value = html

    // Update page title
    document.title = data.summary || data.name || 'App'

    // Write to iframe after next tick
    setTimeout(() => {
      if (iframeRef.value && htmlContent.value) {
        const doc = iframeRef.value.contentDocument
        if (doc) {
          doc.open()
          doc.write(htmlContent.value)
          doc.close()
        }
      }
    }, 0)
  } catch (err: unknown) {
    console.error('Failed to load app:', err)
    const axiosError = err as { response?: { status?: number; data?: { message?: string } } }
    if (axiosError.response?.status === 404) {
      error.value = 'not_found'
    } else {
      error.value = axiosError.response?.data?.message || 'Failed to load app'
    }
  } finally {
    isLoading.value = false
  }
}

function getDefaultHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>App</title>
</head>
<body>
    <h1>App Content Not Available</h1>
    <p>This app has no content defined.</p>
</body>
</html>`
}

function goToLogin() {
  router.push({
    path: '/login',
    query: { redirect: route.fullPath }
  })
}

function goHome() {
  router.push('/')
}
</script>

<template>
  <div class="web-app-view">
    <!-- Loading state -->
    <div v-if="isLoading" class="loading-state">
      <ProgressSpinner />
      <p>Loading app...</p>
    </div>

    <!-- Auth required -->
    <div v-else-if="needsAuth || error === 'auth_required'" class="error-state">
      <i class="pi pi-lock text-6xl text-color-secondary mb-4"></i>
      <h2>Authentication Required</h2>
      <p class="text-color-secondary mb-4">
        This app requires you to be logged in to access it.
      </p>
      <div class="flex gap-2">
        <Button label="Log In" icon="pi pi-sign-in" @click="goToLogin" />
        <Button label="Go Home" icon="pi pi-home" severity="secondary" outlined @click="goHome" />
      </div>
    </div>

    <!-- Not found -->
    <div v-else-if="error === 'not_found'" class="error-state">
      <i class="pi pi-exclamation-circle text-6xl text-color-secondary mb-4"></i>
      <h2>App Not Found</h2>
      <p class="text-color-secondary mb-4">
        The app you're looking for doesn't exist or hasn't been published.
      </p>
      <Button label="Go Home" icon="pi pi-home" @click="goHome" />
    </div>

    <!-- Other error -->
    <div v-else-if="error" class="error-state">
      <i class="pi pi-times-circle text-6xl text-color-secondary mb-4"></i>
      <h2>Error Loading App</h2>
      <p class="text-color-secondary mb-4">{{ error }}</p>
      <div class="flex gap-2">
        <Button label="Try Again" icon="pi pi-refresh" @click="loadApp" />
        <Button label="Go Home" icon="pi pi-home" severity="secondary" outlined @click="goHome" />
      </div>
    </div>

    <!-- App iframe -->
    <iframe
      v-else
      ref="iframeRef"
      class="app-iframe"
      sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-popups allow-downloads"
      title="Published App"
    />
  </div>
</template>

<style scoped>
.web-app-view {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 9999;
  background-color: white;
}

.loading-state,
.error-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  text-align: center;
  padding: 2rem;
}

.error-state h2 {
  margin: 0 0 0.5rem 0;
  color: var(--p-text-color);
}

.app-iframe {
  width: 100%;
  height: 100%;
  border: none;
}
</style>
