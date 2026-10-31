import { computed, ref } from 'vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard } from '@/types/marketplace'

const marketplaceApiBaseUrl = `${publicAppConfig.apiBaseUrl}api/public/marketplace`

export function resolveMarketplaceAssetUrl(url?: string | null) {
  if (!url) {
    return ''
  }

  if (
    url.startsWith('http') ||
    url.startsWith('data:') ||
    url.startsWith('blob:') ||
    url.startsWith('/assets/') ||
    url.startsWith('/src/')
  ) {
    return url
  }

  return `${publicAppConfig.apiBaseUrl}${url.replace(/^\/+/, '')}`
}

export async function fetchList<T>(url: string): Promise<T[]> {
  const response = await fetch(url)
  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}.`)
  }

  const body: unknown = await response.json()
  if (!Array.isArray(body)) {
    throw new Error('Expected a list response.')
  }

  return body as T[]
}

export function useMarketplaceApps(defaultTake = 10) {
  const categories = ref<string[]>([])
  const apps = ref<MarketplaceAppCard[]>([])
  const activeCategory = ref('')
  const search = ref('')
  const loading = ref(false)
  const error = ref<unknown>(null)

  const featuredApps = computed(() => apps.value.filter((app) => app.isFeatured))

  async function loadCategories() {
    try {
      categories.value = await fetchList<string>(`${marketplaceApiBaseUrl}/categories`)
    } catch {
      categories.value = []
    }
  }

  async function loadApps(take = defaultTake) {
    loading.value = true
    error.value = null

    try {
      const query = new URLSearchParams()

      if (activeCategory.value) {
        query.set('category', activeCategory.value)
      }

      if (search.value.trim()) {
        query.set('search', search.value.trim())
      }

      query.set('take', String(take))

      apps.value = await fetchList<MarketplaceAppCard>(`${marketplaceApiBaseUrl}/apps?${query.toString()}`)
    } catch (err) {
      error.value = err
      apps.value = []
    } finally {
      loading.value = false
    }
  }

  return {
    activeCategory,
    apps,
    categories,
    error,
    featuredApps,
    loading,
    loadApps,
    loadCategories,
    search,
  }
}
