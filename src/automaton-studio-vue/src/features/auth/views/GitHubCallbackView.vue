<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
import ProgressSpinner from 'primevue/progressspinner'
import Message from 'primevue/message'
import Button from 'primevue/button'
import { useAuthStore } from '../stores/auth.store'
import { authService } from '../services/auth.service'
const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()

const errorMessage = ref('')
const failureTitle = ref('GitHub sign-in failed')
const registrationPaused = ref(false)
const registrationPausedMessage = 'Public registration is temporarily paused while we prepare Automaton for wider access.'

function readTokenFromHash(): string | null {
  const hash = window.location.hash.startsWith('#') ? window.location.hash.slice(1) : ''
  const params = new URLSearchParams(hash)
  const accessToken = params.get('accessToken')

  if (!accessToken) {
    return null
  }

  return accessToken
}

function readRedirectTarget(): string {
  const hash = window.location.hash.startsWith('#') ? window.location.hash.slice(1) : ''
  const params = new URLSearchParams(hash)
  const redirect = params.get('redirect')
  return redirect && redirect.startsWith('/') ? redirect : '/'
}

onMounted(async () => {
  const callbackError = typeof route.query.error === 'string' ? route.query.error : ''
  if (callbackError) {
    if (callbackError === registrationPausedMessage) {
      registrationPaused.value = true
      failureTitle.value = 'Public registration paused'
      errorMessage.value = registrationPausedMessage
    } else if (callbackError.toLowerCase().includes('cancel')) {
      failureTitle.value = 'GitHub sign-in was cancelled'
      errorMessage.value = 'You can try again whenever you are ready.'
    } else {
      errorMessage.value = 'We couldn’t complete sign-in with GitHub. Please try again.'
    }
    authStore.clearError()
    window.history.replaceState({}, document.title, route.path)
    return
  }

  const token = readTokenFromHash()
  if (!token) {
    errorMessage.value = 'We couldn’t complete sign-in with GitHub. Please try again.'
    return
  }

  try {
    authService.storeAccessToken(token)
    await authStore.completeExternalLogin({ accessToken: token })

    if (!authStore.user) {
      throw new Error('Failed to load your profile after GitHub login.')
    }

    const redirect = readRedirectTarget()
    window.history.replaceState({}, document.title, route.path)
    await router.replace(redirect)
  } catch (error) {
    void authStore.logout()
    errorMessage.value = 'We couldn’t complete sign-in with GitHub. Please try again.'
  }
})

function retryGitHubLogin() {
  authService.startGitHubLogin('/')
}

async function goToLogin() {
  await router.replace('/login')
}
</script>

<template>
  <div class="github-callback-view flex align-items-center justify-content-center min-h-screen">
    <Card class="github-callback-card w-full" style="max-width: 400px">
      <template #content>
        <div class="flex flex-column align-items-center gap-3 text-center py-4">
          <template v-if="errorMessage">
            <div class="github-failure-icon flex align-items-center justify-content-center">
              <i class="pi pi-exclamation-circle"></i>
            </div>
            <h1 class="text-xl font-semibold m-0">{{ failureTitle }}</h1>
            <Message severity="error" :closable="false" class="w-full github-failure-message">
              {{ errorMessage }}
            </Message>
            <p v-if="!registrationPaused" class="m-0 text-color-secondary github-failure-help">
              If this keeps happening, return to email sign-in or try GitHub again.
            </p>
            <div class="github-failure-actions flex flex-column gap-2 w-full">
              <Button
                v-if="!registrationPaused"
                type="button"
                label="Try GitHub Again"
                icon="pi pi-github"
                class="w-full"
                @click="retryGitHubLogin"
              />
              <Button
                type="button"
                label="Back to Sign In"
                severity="secondary"
                outlined
                class="w-full"
                @click="goToLogin"
              />
            </div>
          </template>
          <template v-else>
            <ProgressSpinner style="width: 3rem; height: 3rem" stroke-width="4" />
            <h1 class="text-xl font-semibold m-0">Signing you in with GitHub</h1>
            <p class="m-0 text-color-secondary">Finalizing your Automaton Studio session.</p>
          </template>
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.github-callback-view {
  background-color: var(--p-surface-50);
}

:global(.app-dark) .github-callback-view {
  background-color: var(--p-surface-950);
}

.github-callback-card {
  margin: 1rem;
}

.github-failure-icon {
  width: 4rem;
  height: 4rem;
  border-radius: 9999px;
  background: color-mix(in srgb, var(--p-red-500) 12%, transparent);
  color: var(--p-red-500);
  font-size: 1.75rem;
}

.github-failure-message {
  text-align: left;
}

.github-failure-help {
  line-height: 1.5;
}

.github-failure-actions {
  margin-top: 0.5rem;
}
</style>
