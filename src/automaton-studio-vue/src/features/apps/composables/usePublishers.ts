import { reactive } from 'vue'
import { appsService } from '../services/apps.service'
import type { CatalogPublisher } from '../types/apps.types'

/** Publisher of each installed catalog app, by app id; filled from the catalog once per page load. */
const byApp = reactive(new Map<string, CatalogPublisher>())
let loading: Promise<void> | null = null

async function load() {
  try {
    const response = await appsService.getCatalog(false, { installed: true, pageSize: 100 })
    for (const entry of response.apps) if (entry.installedAppId && entry.publisher) byApp.set(entry.installedAppId, entry.publisher)
  } catch {
    // The badge is a nicety: without the catalog the cards simply show no publisher.
  }
}

export function usePublishers() {
  loading ??= load()
  return { publisherOf: (appId: string): CatalogPublisher | undefined => byApp.get(appId) }
}
