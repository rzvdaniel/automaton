import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { homeService } from '../services/home.service'
import type { HomeCard, HomePinnedApp } from '../types/home.types'

/** Home data for the signed-in person: Needs-you cards and pinned apps. */
export const useHomeStore = defineStore('home', () => {
  const needsYou = ref<HomeCard[]>([])
  const pinned = ref<HomePinnedApp[]>([])
  const isLoading = ref(false)
  const loaded = ref(false)
  const error = ref<string | null>(null)

  const pinnedIds = computed(() => new Set(pinned.value.map((item) => item.app.id)))

  async function load() {
    isLoading.value = true
    error.value = null
    try {
      const data = await homeService.get()
      needsYou.value = data.needsYou
      pinned.value = data.pinned
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

  async function dismiss(cardKey: string) {
    await homeService.dismiss(cardKey)
    needsYou.value = needsYou.value.filter((card) => card.key !== cardKey)
  }

  return { needsYou, pinned, pinnedIds, isLoading, loaded, error, load, ensureLoaded, pin, unpin, reorder, dismiss }
})
