import { ref } from 'vue'

// The official catalog of installable app sources, read from its public GitHub repository.
export const OFFICIAL_CATALOG_REPO = 'rzvdaniel/automaton-apps'
const CATALOG_URL = `https://raw.githubusercontent.com/${OFFICIAL_CATALOG_REPO}/main/catalog.json`

interface CatalogFileEntry {
  id: string
  name: string
  summary?: string
  category?: string
  folder: string
  commit?: string
  minServerVersion?: string
  permissions?: string[]
}

export interface CatalogEntry {
  id: string
  name: string
  summary: string
  category: string
  folder: string
  minServerVersion: string
  permissions: string[]
  sourceUrl: string
  initials: string
}

function initialsOf(name: string) {
  const words = name.replace(/^Automaton\s+/i, '').split(/\s+/).filter(Boolean)
  return words.slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?'
}

export function toCatalogEntries(file: { repo?: string; apps?: CatalogFileEntry[] }): CatalogEntry[] {
  const repo = file.repo || OFFICIAL_CATALOG_REPO
  return (file.apps ?? [])
    .filter((a) => a && a.id && a.name && a.folder)
    .map((a) => ({
      id: a.id,
      name: a.name.replace(/^Automaton\s+/i, ''),
      summary: a.summary ?? '',
      category: a.category || 'Other',
      folder: a.folder,
      minServerVersion: a.minServerVersion ?? '',
      permissions: a.permissions ?? [],
      sourceUrl: `https://github.com/${repo}/tree/${a.commit || 'main'}/${a.folder}`,
      initials: initialsOf(a.name),
    }))
}

export function useCatalog() {
  const apps = ref<CatalogEntry[]>([])
  const loading = ref(true)
  const error = ref(false)

  async function load() {
    loading.value = true
    error.value = false
    try {
      const response = await fetch(CATALOG_URL, { cache: 'no-cache', signal: AbortSignal.timeout(10000) })
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      apps.value = toCatalogEntries(await response.json())
    } catch {
      error.value = true
    } finally {
      loading.value = false
    }
  }

  return { apps, loading, error, load }
}
