<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

declare global {
  interface Window {
    turnstile?: {
      render: (
        element: HTMLElement,
        options: {
          sitekey: string
          callback?: (token: string) => void
          'error-callback'?: () => void
          'expired-callback'?: () => void
          theme?: 'light' | 'dark' | 'auto'
        },
      ) => string
      reset: (widgetId: string) => void
      remove: (widgetId: string) => void
    }
  }
}

const props = defineProps<{
  siteKey: string
  theme?: 'light' | 'dark' | 'auto'
}>()

const emit = defineEmits<{
  (e: 'verified', token: string): void
  (e: 'expired'): void
  (e: 'error'): void
}>()

const containerRef = ref<HTMLElement | null>(null)
let widgetId: string | null = null

function ensureScript(): Promise<void> {
  return new Promise((resolve, reject) => {
    if (window.turnstile) {
      resolve()
      return
    }

    const existingScript = document.querySelector<HTMLScriptElement>('script[data-turnstile-script="true"]')
    if (existingScript) {
      existingScript.addEventListener('load', () => resolve(), { once: true })
      existingScript.addEventListener('error', () => reject(new Error('Failed to load Cloudflare Turnstile script.')), {
        once: true,
      })
      return
    }

    const script = document.createElement('script')
    script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'
    script.async = true
    script.defer = true
    script.dataset.turnstileScript = 'true'
    script.addEventListener('load', () => resolve(), { once: true })
    script.addEventListener('error', () => reject(new Error('Failed to load Cloudflare Turnstile script.')), { once: true })
    document.head.appendChild(script)
  })
}

async function renderWidget() {
  if (!props.siteKey || !containerRef.value) {
    return
  }

  await ensureScript()

  if (!window.turnstile || !containerRef.value) {
    return
  }

  widgetId = window.turnstile.render(containerRef.value, {
    sitekey: props.siteKey,
    theme: props.theme ?? 'auto',
    callback: (token: string) => emit('verified', token),
    'expired-callback': () => emit('expired'),
    'error-callback': () => emit('error'),
  })
}

function reset() {
  if (widgetId && window.turnstile) {
    window.turnstile.reset(widgetId)
  }
}

defineExpose({ reset })

onMounted(() => {
  void renderWidget()
})

onBeforeUnmount(() => {
  if (widgetId && window.turnstile) {
    window.turnstile.remove(widgetId)
  }
})
</script>

<template>
  <div class="turnstile-container">
    <div ref="containerRef"></div>
  </div>
</template>

<style scoped>
.turnstile-container {
  display: flex;
  justify-content: center;
  width: 100%;
}
</style>
