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
  tags?: string[]
  icon?: string
  screenshots?: string[]
  license?: string
  recommended?: boolean
  recommendedOrder?: number
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
  repo: string
  commit: string
  tags: string[]
  license: string
  icon: string | null
  screenshots: string[]
  recommended: boolean
  recommendedOrder: number
}

const SAFE_PATH = /^[A-Za-z0-9_.-]+(\/[A-Za-z0-9_.-]+)*$/
const safe = (v: string) => SAFE_PATH.test(v) && v.split('/').every((p) => p !== '.' && p !== '..')

// An icon or screenshot is a path in the app folder, loaded from the pinned commit; https:// addresses are kept, anything unsafe is dropped.
export function assetUrl(repo: string, folder: string, commit: string | undefined, path: string | undefined): string | null {
  const p = path?.trim()
  if (!p) return null
  if (/^https:\/\//i.test(p)) return p
  const f = folder.replace(/^\/+|\/+$/g, '')
  if (!safe(p) || !safe(f) || !safe(repo) || !commit || !/^[0-9a-f]{40}$/.test(commit)) return null
  return `https://raw.githubusercontent.com/${repo}/${commit}/${f}/${p}`
}

// The address of Studio's install screen for one catalog app.
export function studioInstallUrl(studio: string, app: Pick<CatalogEntry, 'repo' | 'folder' | 'commit'>): string {
  const q = new URLSearchParams({ repo: app.repo, folder: app.folder, commit: app.commit })
  return `${studio}/apps/install-source?${q}`
}

// Accepts "studio.example.com" or a full address; only http(s) origins are kept.
export function normalizeStudioAddress(input: string): string | null {
  const text = input.trim()
  if (!text) return null
  try {
    const url = new URL(/^[a-z][a-z0-9+.-]*:\/\//i.test(text) ? text : `https://${text}`)
    if (url.protocol !== 'https:' && url.protocol !== 'http:') return null
    return url.origin
  } catch {
    return null
  }
}

export function matchesSearch(app: CatalogEntry, query: string): boolean {
  const q = query.trim().toLowerCase()
  if (!q) return true
  return [app.name, app.summary, app.category, ...app.tags].some((t) => t.toLowerCase().includes(q))
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
      repo,
      commit: a.commit ?? '',
      tags: (a.tags ?? []).filter((t) => typeof t === 'string'),
      license: a.license ?? '',
      icon: assetUrl(repo, a.folder, a.commit, a.icon),
      screenshots: (a.screenshots ?? []).map((x) => assetUrl(repo, a.folder, a.commit, x)).filter((x): x is string => !!x).slice(0, 8),
      recommended: a.recommended === true,
      recommendedOrder: a.recommendedOrder ?? Number.MAX_SAFE_INTEGER,
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
