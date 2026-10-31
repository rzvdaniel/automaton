import { ref } from 'vue'
import { publicAppConfig } from '@/config/publicAppConfig'

// Unknown status (older API, network error) keeps the store available; catalog calls report their own errors.
export async function fetchStoreEnabled(apiBaseUrl = publicAppConfig.apiBaseUrl): Promise<boolean> {
  try {
    const response = await fetch(`${apiBaseUrl}api/public/store/status`)
    if (!response.ok) return true
    const body: unknown = await response.json()
    return !(typeof body === 'object' && body !== null && (body as { enabled?: unknown }).enabled === false)
  } catch {
    return true
  }
}

export function useStoreStatus() {
  const checked = ref(false)
  const enabled = ref(true)

  async function load() {
    enabled.value = await fetchStoreEnabled()
    checked.value = true
  }

  return { checked, enabled, load }
}
