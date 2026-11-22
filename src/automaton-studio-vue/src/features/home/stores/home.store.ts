import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { homeService } from '../services/home.service'
import { notificationsService } from '../services/notifications.service'
import type { HomeCard, HomePinnedApp, NotificationItem } from '../types/home.types'

/** Home data for the signed-in person: Needs-you cards and pinned apps. */
export const useHomeStore = defineStore('home', () => {
  const needsYou = ref<HomeCard[]>([])
  const pinned = ref<HomePinnedApp[]>([])
  const listView = ref(false)
  const notifications = ref<NotificationItem[]>([])
  const isLoading = ref(false)
  const loaded = ref(false)
  const error = ref<string | null>(null)

  const pinnedIds = computed(() => new Set(pinned.value.map((item) => item.app.id)))

  async function load() {
    isLoading.value = true
    error.value = null
    try {
      // The feed is secondary: Home still opens when it cannot be read.
      const [data, feed] = await Promise.all([homeService.get(), notificationsService.list().catch(() => [] as NotificationItem[])])
      notifications.value = feed
      needsYou.value = data.needsYou
      pinned.value = data.pinned
      listView.value = data.listView === true
      loaded.value = true
    } catch {
      error.value = 'Home could not be loaded.'
    } finally {
      isLoading.value = false
    }
  }

  async function ensureLoaded() {
    if (!loaded.value) await load()
  }

  async function pin(appId: string) {
    await homeService.pin(appId)
    await load()
  }

  async function unpin(appId: string) {
    await homeService.unpin(appId)
    pinned.value = pinned.value.filter((item) => item.app.id !== appId)
  }

  async function reorder(appIds: string[]) {
    const byId = new Map(pinned.value.map((item) => [item.app.id, item]))
    const previous = pinned.value
    pinned.value = appIds.map((id) => byId.get(id)).filter((item): item is HomePinnedApp => !!item)
    try {
      await homeService.reorder(appIds)
    } catch (caught) {
      pinned.value = previous
      throw caught
    }
  }

  async function setWide(appId: string, wide: boolean) {
    const item = pinned.value.find((entry) => entry.app.id === appId)
    if (!item) return
    const previous = item.wide
    item.wide = wide
    try {
      await homeService.setSize(appId, wide)
    } catch (caught) {
      item.wide = previous
      throw caught
    }
  }

  async function setListView(value: boolean) {
    const previous = listView.value
    listView.value = value
    try {
      await homeService.setView(value)
    } catch (caught) {
      listView.value = previous
      throw caught
    }
  }

  /** Runs the app's card once and reloads Home until its "Updated" time moves (or about 40 seconds pass). */
  const refreshing = ref<Set<string>>(new Set())
  async function refreshCard(appId: string) {
    refreshing.value = new Set(refreshing.value).add(appId)
    try {
      await homeService.refreshCard(appId)
      const before = pinned.value.find((item) => item.app.id === appId)?.card?.updatedAtUtc
      for (let i = 0; i < 20; i++) {
        await new Promise((resolve) => setTimeout(resolve, 2000))
        const data = await homeService.get()
        pinned.value = data.pinned
        needsYou.value = data.needsYou
        if (data.pinned.find((item) => item.app.id === appId)?.card?.updatedAtUtc !== before) break
      }
    } finally {
      const next = new Set(refreshing.value)
      next.delete(appId)
      refreshing.value = next
    }
  }

  async function dismiss(cardKey: string) {
    await homeService.dismiss(cardKey)
    needsYou.value = needsYou.value.filter((card) => card.key !== cardKey)
  }

  async function acknowledge(key: string) {
    await notificationsService.acknowledge(key)
    notifications.value = notifications.value.filter((item) => item.key !== key)
  }

  return { listView, setWide, setListView, notifications, acknowledge, needsYou, pinned, pinnedIds, isLoading, loaded, error, refreshing, refreshCard, load, ensureLoaded, pin, unpin, reorder, dismiss }
})
