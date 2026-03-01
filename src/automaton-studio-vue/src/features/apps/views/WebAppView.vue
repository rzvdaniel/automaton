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
    const { data } = await apiClient.get(`/api/web/${slug.value}`)

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
    const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
    // Pass the current auth token so authenticated apps can call protected API endpoints.
    // Anonymous apps get null and use credentials:include (cookie fallback).
    const authToken = authStore.accessToken
    const publishedScript = `
<script>
(function(window, APP_SLUG, BASE_URL, AUTH_TOKEN) {
  'use strict';

  console.log('[AutomatonAPI] Published app loaded:', APP_SLUG);

  async function apiCall(path, method, body) {
    const url = BASE_URL + path;
    console.log('[AutomatonAPI] ' + method + ' ' + url);

    const headers = { 'Content-Type': 'application/json' };
    if (AUTH_TOKEN) {
      headers['Authorization'] = 'Bearer ' + AUTH_TOKEN;
    }

    const options = {
      method: method,
      headers: headers,
      credentials: AUTH_TOKEN ? 'same-origin' : 'include'
    };

    if (body) {
      options.body = JSON.stringify(body);
    }

    const response = await fetch(url, options);
    const data = await response.json();

    if (!response.ok) {
      const error = new Error(data.error || data.message || 'Request failed');
      error.response = data;
      throw error;
    }

    return data;
  }

  async function executeFlow(flowIdentifier, options) {
    options = options || {};
    const path = '/api/web-apps/' + encodeURIComponent(APP_SLUG) + '/flows/' + encodeURIComponent(flowIdentifier) + '/execute';
    const body = { inputs: options.inputs || {}, runnerNames: options.runnerNames || null };
    return apiCall(path, 'POST', body);
  }

  async function executeController(controllerName, actionName, options) {
    options = options || {};
    const path = '/api/web-apps/' + encodeURIComponent(APP_SLUG) + '/controllers/' + encodeURIComponent(controllerName) + '/actions/' + encodeURIComponent(actionName) + '/execute';
    const body = { inputs: options.inputs || {}, runnerNames: options.runnerNames || null };
    return apiCall(path, 'POST', body);
  }

  window.automatonApi = {
    executeFlow: executeFlow,
    executeController: executeController,
    getAppId: function() { return null; },
    getAppSlug: function() { return APP_SLUG; }
  };

  console.log('[AutomatonAPI] Ready!');

})(window, '${slug.value}', '${baseUrl}', ${authToken ? `'${authToken}'` : 'null'});
<\/script>`

    // Get HTML content
    let html = data.htmlContent || getDefaultHtml()

    // Strip any existing automatonApi injection to prevent double-injection
    html = html.replace(/<script\b[^>]*>[\s\S]*?\(function\s*\(\s*window\s*,\s*APP_(?:ID|SLUG)\b[\s\S]*?<\/script>/gi, '')

    // Inject base tag for resolving relative paths to app files
    const appBaseUrl = `${baseUrl}/apps/${slug.value}/`
    const baseTag = `<base href="${appBaseUrl}">`

    // Add cache-busting using app version/updated timestamp for published apps
    // This ensures files are cached between page loads but refresh when app is republished
    const cacheBuster = data.version || new Date(data.updated || Date.now()).getTime()
    html = html.replace(
      /(<link[^>]+href=["'])([^"'?]+\.css)(["'])/gi,
      `$1$2?v=${cacheBuster}$3`
    )
    html = html.replace(
      /(<script[^>]+src=["'])([^"'?]+\.js)(["'])/gi,
      `$1$2?v=${cacheBuster}$3`
    )

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
      sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-popups"
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
