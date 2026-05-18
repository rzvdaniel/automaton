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

export function useMarketplaceApps(defaultTake = 10) {
  const categories = ref<string[]>([])
  const apps = ref<MarketplaceAppCard[]>([])
  const activeCategory = ref('')
  const search = ref('')
  const loading = ref(false)
  const error = ref<unknown>(null)

  const featuredApps = computed(() => apps.value.filter((app) => app.isFeatured))

  async function loadCategories() {
    const response = await fetch(`${marketplaceApiBaseUrl}/categories`)
    categories.value = await response.json()
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

      const response = await fetch(`${marketplaceApiBaseUrl}/apps?${query.toString()}`)
      apps.value = await response.json()
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
