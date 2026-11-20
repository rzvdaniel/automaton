<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import { useToast } from 'primevue/usetoast'
import AppMark from '@/features/apps/components/AppMark.vue'
import { getStudioApplicationUrl } from '@/features/apps/utils/public-app-url'
import { appState } from '@/features/apps/utils/app-list-display'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { serverConsoleService } from '@/features/server/services/server-console.service'
import type { HomeCard, HomePinnedApp } from '../types/home.types'
import PublisherBadge from '@/features/apps/components/PublisherBadge.vue'
import { usePublishers } from '@/features/apps/composables/usePublishers'
import HomeAppCard from '../components/HomeAppCard.vue'
import { useHomeStore } from '../stores/home.store'
import { footerLine, moved, mosaic, slotAt, type MosaicPlace, type SlotRect } from '../utils'
import PermissionRequestRow from '@/features/admin/components/PermissionRequestRow.vue'
import { usePermissionRequests } from '@/features/admin/composables/usePermissionRequests'

const router = useRouter()
const store = useHomeStore()
const { publisherOf } = usePublishers()
const auth = useAuthStore()
const toast = useToast()

const isAdmin = computed(() => auth.user?.roles?.includes('SysAdmin') === true)
const empty = computed(() => store.pinned.length === 0 && store.needsYou.length === 0 && store.notifications.length === 0)

const permissions = usePermissionRequests()
const visibleRequests = computed(() => permissions.requests.value.slice(0, 3))
const hasPermissionCard = computed(() => store.needsYou.some((card) => card.kind === 'permission-requests'))
onMounted(async () => {
  await store.load()
  // The card names each request; it needs the list only when the server says some wait.
  if (hasPermissionCard.value && isAdmin.value) await permissions.load()
})

async function acknowledge(key: string) {
  try {
    await store.acknowledge(key)
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'The notification could not be acknowledged.', life: 5000 })
  }
}

async function refreshCard(appId: string) {
  try {
    await store.refreshCard(appId)
  } catch (caught) {
    const response = (caught as { response?: { status?: number; data?: { message?: string } } }).response
    const polite = response?.status === 429 || response?.status === 403
    toast.add({
      severity: polite ? 'info' : 'error', summary: polite ? 'Not now' : 'Error',
      detail: response?.data?.message ?? 'The card could not be refreshed.', life: 5000,
    })
  }
}

async function dismiss(key: string) {
  try {
    await store.dismiss(key)
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'The card could not be dismissed.', life: 5000 })
  }
}

const installing = ref<string | null>(null)
/** Cards whose app was just installed here: card key to the new app's id. The card stays and offers Open. */
const installed = ref<Record<string, string>>({})
const isInstalled = (card: HomeCard) => card.key in installed.value
/** Installed cards whose app is running; Open is offered only then (before, the first deploy is still going). */
const live = ref<Set<string>>(new Set())
const isLive = (card: HomeCard) => live.value.has(card.key)
const isStarting = (card: HomeCard) => isInstalled(card) && !isLive(card)
const pollMs = 3000
const pollLimit = 100
async function waitUntilRunning(key: string) {
  for (let i = 0; i < pollLimit; i++) {
    await new Promise((resolve) => setTimeout(resolve, pollMs))
    try {
      if ((await serverConsoleService.get()).running) break
    } catch {
      // keep waiting; a brief API error should not strand the card
    }
  }
  // After the limit Open is offered anyway: the app page shows what the deploy is doing.
  live.value = new Set(live.value).add(key)
}
const installedTitle = (card: HomeCard) => `${card.title.replace(/^Install /, '')} is installed`
/** Server Console installs in place, then the card offers Open; any other recommended app opens in the catalog. */
async function act(card: HomeCard) {
  if (isStarting(card)) return
  if (isInstalled(card)) {
    window.open(getStudioApplicationUrl(installed.value[card.key]), '_blank', 'noopener')
    return
  }
  if (card.kind !== 'recommended-app' || card.catalogAppId !== 'server-console') return router.push(card.actionPath)
  installing.value = card.key
  try {
    const { appId, running } = await serverConsoleService.install()
    if (appId) {
      installed.value = { ...installed.value, [card.key]: appId }
      if (running) live.value = new Set(live.value).add(card.key)
      else void waitUntilRunning(card.key)
    } else await store.load()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Server Console could not be installed.', life: 5000 })
  } finally {
    installing.value = null
  }
}

/** Calm by default; orange when the card or app needs attention; red only when failing. */
function attention(item: HomePinnedApp): 'calm' | 'attention' | 'failing' {
  const tone = item.card?.card.tone
  if (appState(item.app).tone === 'failing' || tone === 'critical') return 'failing'
  return tone === 'warning' ? 'attention' : 'calm'
}
const stateText = (item: HomePinnedApp) => {
  const label = appState(item.app).label
  return label.charAt(0).toUpperCase() + label.slice(1)
}

// Reordering: pointer drag from the card's head row (5 px threshold), arrow buttons, or Alt+Arrow.
const announcement = ref('')
async function save(ids: string[]) {
  try {
    await store.reorder(ids)
    return true
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'The order could not be saved.', life: 5000 })
    return false
  }
}
async function shift(index: number, by: number) {
  const target = index + by
  const item = store.pinned[index]
  if (!item || target < 0 || target >= store.pinned.length) return
  // Moving the card re-inserts it in the DOM, which drops focus; put it back afterwards.
  const focused = document.activeElement as HTMLElement | null
  const saved = await save(moved(store.pinned.map((entry) => entry.app.id), index, target))
  await nextTick()
  focused?.focus?.()
  if (saved) announcement.value = `${item.app.name} moved to position ${target + 1} of ${store.pinned.length}`
}
function onCardKey(event: KeyboardEvent, index: number) {
  if (!event.altKey || event.ctrlKey || event.metaKey) return
  const by = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : 0
  if (by === 0) return
  event.preventDefault()
  void shift(index, by)
}

const dragThreshold = 5
interface DragState { index: number; x: number; y: number; offsetX: number; offsetY: number }
const dragging = ref<DragState | null>(null)
/** While dragging: the order the cards would have, as indexes into store.pinned. */
const order = ref<number[] | null>(null)
const settling = ref<string | null>(null)
let pending: { index: number; startX: number; startY: number } | null = null
let swallowNextClick = false

function pointOnBoard(event: PointerEvent) {
  const rect = board.value!.getBoundingClientRect()
  return { x: event.clientX - rect.left, y: event.clientY - rect.top }
}
function slotRects(sequence: number[]): SlotRect[] {
  const width = (board.value!.clientWidth - (columns.value - 1) * gap) / columns.value
  const rects: SlotRect[] = []
  sequence.forEach((index) => {
    const place = places.value[index]
    if (!place) return
    rects[index] = { left: place.column * (width + gap), top: place.top, width: place.span * width + (place.span - 1) * gap, height: cardEls.value[index]?.offsetHeight ?? 0 }
  })
  return rects
}
function onPointerDown(event: PointerEvent, index: number) {
  if (phone.value || event.button !== 0 || !measured.value || store.pinned.length < 2) return
  pending = { index, startX: event.clientX, startY: event.clientY }
  window.addEventListener('pointermove', onPointerMove)
  window.addEventListener('pointerup', onPointerUp)
  window.addEventListener('pointercancel', onPointerCancel)
}
function onPointerMove(event: PointerEvent) {
  if (!pending || !board.value) return
  if (!dragging.value) {
    if (Math.hypot(event.clientX - pending.startX, event.clientY - pending.startY) < dragThreshold) return
    const card = cardEls.value[pending.index]!.getBoundingClientRect()
    dragging.value = { index: pending.index, x: 0, y: 0, offsetX: pending.startX - card.left, offsetY: pending.startY - card.top }
    order.value = store.pinned.map((_, index) => index)
    swallowNextClick = true
  }
  const drag = dragging.value
  const point = pointOnBoard(event)
  drag.x = point.x - drag.offsetX
  drag.y = point.y - drag.offsetY
  const sequence = order.value!
  const target = slotAt(slotRects(sequence), point.x, point.y, drag.index)
  if (target >= 0) {
    const from = sequence.indexOf(drag.index)
    const to = sequence.indexOf(target)
    if (from !== to) {
      order.value = moved(sequence, from, to)
      relayout()
    }
  }
}
function stopListening() {
  pending = null
  window.removeEventListener('pointermove', onPointerMove)
  window.removeEventListener('pointerup', onPointerUp)
  window.removeEventListener('pointercancel', onPointerCancel)
  setTimeout(() => { swallowNextClick = false }, 0)
}
async function endDrag(commit: boolean) {
  const drag = dragging.value
  const sequence = order.value
  stopListening()
  if (!drag || !sequence) return
  const id = store.pinned[drag.index]!.app.id
  const ids = sequence.map((index) => store.pinned[index]!.app.id)
  const changed = sequence.some((index, position) => index !== position)
  settling.value = id
  setTimeout(() => { if (settling.value === id) settling.value = null }, 260)
  dragging.value = null
  if (!commit || !changed) {
    order.value = null
    relayout()
    return
  }
  // The store reorders synchronously (and rolls back on failure), so clear the preview in the same tick.
  const saved = save(ids)
  order.value = null
  if (await saved) announcement.value = `${store.pinned.find((item) => item.app.id === id)?.app.name ?? 'Card'} moved to position ${ids.indexOf(id) + 1} of ${ids.length}`
}
const onPointerUp = () => { void endDrag(true) }
const onPointerCancel = () => { void endDrag(false) }
/** A drag ends over the card's link; the click that follows must not open the app. */
function swallowClick(event: MouseEvent) {
  if (!swallowNextClick) return
  event.preventDefault()
  event.stopPropagation()
}
const slotStyle = computed(() => {
  const drag = dragging.value
  const place = drag ? places.value[drag.index] : undefined
  if (!drag || !place) return undefined
  return { ...placeStyle(place), height: `${cardEls.value[drag.index]?.offsetHeight ?? 0}px` }
})

async function toggleWide(appId: string, wide: boolean) {
  try {
    await store.setWide(appId, wide)
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'The size could not be saved.', life: 5000 })
  }
}
async function setView(list: boolean) {
  try {
    await store.setListView(list)
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'The view could not be saved.', life: 5000 })
  }
}

// Mosaic: measure the cards and put each one in the shortest column. Falls back to a plain grid until measured.
// Cards are absolutely placed, and CSS transitions on top/left/width give the slide when the order changes.
const gap = 12
const minCard = 280
const phoneQuery = '(max-width: 559px)'
const phone = ref(false)
const board = ref<HTMLElement | null>(null)
const cardEls = ref<HTMLElement[]>([])
const places = ref<MosaicPlace[]>([])
const columns = ref(1)
const boardHeight = ref(0)
const measured = ref(false)
function relayout() {
  const el = board.value
  if (!el || store.listView) return
  columns.value = phone.value ? 1 : Math.max(1, Math.floor((el.clientWidth + gap) / (minCard + gap)))
  const sequence = order.value ?? store.pinned.map((_, index) => index)
  const heights = sequence.map((index) => cardEls.value[index]?.offsetHeight ?? 0)
  if (heights.some((height) => height === 0)) return
  const result = mosaic(heights, sequence.map((index) => store.pinned[index]?.wide === true), columns.value, gap)
  const byIndex: MosaicPlace[] = []
  sequence.forEach((index, position) => { byIndex[index] = result.places[position]! })
  places.value = byIndex
  boardHeight.value = result.height
  measured.value = true
}
let resizeObserver: ResizeObserver | null = null
function observe() {
  resizeObserver?.disconnect()
  if (typeof ResizeObserver === 'undefined') return
  resizeObserver = new ResizeObserver(relayout)
  if (board.value) resizeObserver.observe(board.value)
  cardEls.value.forEach((el) => el && resizeObserver?.observe(el))
}
function placeStyle(place: MosaicPlace) {
  const width = `calc((100% - ${(columns.value - 1) * gap}px) / ${columns.value})`
  return {
    position: 'absolute' as const, top: `${place.top}px`, width: place.span === 2 ? `calc(${width} * 2 + ${gap}px)` : width,
    left: `calc((${width} + ${gap}px) * ${place.column})`,
  }
}
function cardStyle(index: number) {
  const place = places.value[index]
  if (!measured.value || !place) return undefined
  const drag = dragging.value
  if (drag && drag.index === index) return { ...placeStyle(place), left: `${drag.x}px`, top: `${drag.y}px` }
  return placeStyle(place)
}
let phoneList: MediaQueryList | null = null
function onPhoneChange() {
  phone.value = phoneList?.matches ?? false
  relayout()
}
watch(() => store.listView, () => { measured.value = false })
watch(() => [store.pinned.map((item) => `${item.app.id}${item.wide}`).join(), store.listView], async () => {
  await nextTick()
  observe()
  relayout()
})
onMounted(async () => {
  if (typeof window.matchMedia === 'function') {
    phoneList = window.matchMedia(phoneQuery)
    phone.value = phoneList.matches
    phoneList.addEventListener('change', onPhoneChange)
  }
  await nextTick(); observe(); relayout()
})
onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  phoneList?.removeEventListener('change', onPhoneChange)
  stopListening()
})
</script>

<template>
  <div class="home-view">
    <header class="home-head">
      <h1 class="home-head__title">Home</h1>
      <p class="home-head__line">What needs you, and the apps you pinned.</p>
    </header>

    <div v-if="store.isLoading && !store.loaded" class="home-skeleton" aria-busy="true">
      <Skeleton v-for="i in 3" :key="i" height="84px" border-radius="12px" />
    </div>

    <Message v-else-if="store.error" severity="error" :closable="false">
      <div class="flex align-items-center justify-content-between w-full">
        <span>{{ store.error }}</span>
        <Button label="Retry" severity="danger" size="small" outlined @click="store.load()" />
      </div>
    </Message>

    <template v-else>
      <section v-if="store.notifications.length > 0" aria-labelledby="notifications-title" data-testid="notifications">
        <h2 id="notifications-title" class="home-section">Notifications</h2>
        <div class="home-cards">
          <article
            v-for="item in store.notifications"
            :key="item.key"
            class="need"
            :class="`need--${item.severity}`"
            :data-testid="`notification-${item.kind}`"
          >
            <div class="need__text">
              <p class="need__title">{{ item.title }}</p>
              <p class="need__detail">{{ item.detail }}</p>
            </div>
            <div class="need__actions">
              <Button :label="item.actionLabel" size="small" class="btn-ink" @click="router.push(item.actionPath)" />
              <Button
                v-if="item.acknowledgeable"
                label="Acknowledge"
                size="small"
                text
                severity="secondary"
                :aria-label="`Acknowledge ${item.title}`"
                data-testid="acknowledge-notification"
                @click="acknowledge(item.key)"
              />
            </div>
          </article>
        </div>
      </section>

      <section v-if="store.needsYou.length > 0" aria-labelledby="needs-you-title" data-testid="needs-you">
        <h2 id="needs-you-title" class="home-section">Needs you</h2>
        <div class="home-cards">
          <article v-for="card in store.needsYou" :key="card.key" class="need" :data-testid="`need-${card.kind}`">
            <div class="need__text">
              <p class="need__title">{{ isInstalled(card) ? installedTitle(card) : card.title }}</p>
              <p v-if="!isInstalled(card)" class="need__detail">{{ card.detail }}</p>
              <ul v-if="card.kind === 'permission-requests' && visibleRequests.length > 0" class="need__requests" data-testid="home-permission-requests">
                <li v-for="request in visibleRequests" :key="`${request.appId}:${request.permission}`">
                  <PermissionRequestRow
                    :request="request"
                    :busy="permissions.busy.value"
                    show-app
                    @decide="async (approved) => { await permissions.decide([request], approved); await store.load() }"
                  />
                </li>
              </ul>
              <p v-if="card.kind === 'permission-requests' && permissions.actionFailed.value" class="need__detail" role="alert">The change could not be saved.</p>
            </div>
            <div class="need__actions">
              <Button
                :label="isStarting(card) ? 'Installing…' : isInstalled(card) ? 'Open' : card.kind === 'permission-requests' && permissions.count.value > 3 ? `See all ${permissions.count.value}` : card.actionLabel"
                :icon="isLive(card) ? 'pi pi-external-link' : undefined"
                icon-pos="right"
                size="small"
                class="btn-ink"
                :loading="installing === card.key || isStarting(card)"
                :disabled="installing !== null || isStarting(card)"
                @click="act(card)"
              />
              <Button
                v-if="card.dismissible && !isInstalled(card)"
                label="Dismiss"
                size="small"
                text
                severity="secondary"
                :aria-label="`Dismiss ${card.title}`"
                data-testid="dismiss-card"
                @click="dismiss(card.key)"
              />
            </div>
          </article>
        </div>
      </section>

      <section v-if="store.pinned.length > 0" aria-labelledby="pinned-title" data-testid="pinned-apps">
        <div class="home-section-row">
          <h2 id="pinned-title" class="home-section">Pinned apps</h2>
          <div class="view-switch" role="group" aria-label="Home view">
            <Button label="Cards" size="small" text :severity="store.listView ? 'secondary' : undefined" :aria-pressed="!store.listView" data-testid="view-cards" @click="setView(false)" />
            <Button label="List" size="small" text :severity="store.listView ? undefined : 'secondary'" :aria-pressed="store.listView" data-testid="view-list" @click="setView(true)" />
          </div>
        </div>
        <p class="sr-only" role="status" aria-live="polite" data-testid="move-announcement">{{ announcement }}</p>
        <ul v-if="store.listView" class="pin-list" data-testid="pinned-list">
          <li v-for="item in store.pinned" :key="item.app.id">
            <RouterLink :to="`/apps/${item.app.id}`" class="pin-row" :aria-label="`Open ${item.app.name}`" data-testid="pinned-row">
              <AppMark :id="item.app.id" :name="item.app.name" :image-url="item.app.thumbnailUrl" />
              <span class="pin__name">{{ item.app.name }}</span>
              <PublisherBadge v-if="publisherOf(item.app.id)" :publisher="publisherOf(item.app.id)!" />
              <span class="pin__state" :class="`pin__state--${appState(item.app).tone}`">{{ appState(item.app).label }}</span>
              <span class="pin-row__summary">{{ item.card?.card.status ?? '' }}</span>
            </RouterLink>
          </li>
        </ul>
        <div v-else ref="board" class="home-cards home-cards--apps" :class="{ 'home-cards--mosaic': measured }" :style="measured ? { height: `${boardHeight}px` } : undefined">
          <div v-if="dragging && slotStyle" class="pin-slot" :style="slotStyle" data-testid="drop-slot" aria-hidden="true" />
          <article
            v-for="(item, index) in store.pinned"
            :key="item.app.id"
            :ref="(el) => { if (el) cardEls[index] = el as HTMLElement }"
            class="pin"
            :class="[{ 'pin--wide': item.wide, 'pin--dragging': dragging?.index === index, 'pin--settling': settling === item.app.id }, `pin--${attention(item)}`]"
            :style="cardStyle(index)"
            data-testid="pinned-app"
            @keydown="onCardKey($event, index)"
          >
            <RouterLink
              :to="`/apps/${item.app.id}`" class="pin__link" :aria-label="`Open ${item.app.name}`"
              @pointerdown="onPointerDown($event, index)" @dragstart.prevent @click.capture="swallowClick"
            >
              <AppMark :id="item.app.id" :name="item.app.name" :image-url="item.app.thumbnailUrl" />
              <span class="pin__name">{{ item.app.name }}</span>
              <span class="pin__state" :class="`pin__state--${attention(item)}`" data-testid="pin-state">
                <span class="pin__dot" />{{ stateText(item) }}
              </span>
            </RouterLink>
            <PublisherBadge v-if="publisherOf(item.app.id)" :publisher="publisherOf(item.app.id)!" />
            <HomeAppCard v-if="item.card" :app-id="item.app.id" :item="item.card" />
            <p class="pin__footer" data-testid="app-card-time">{{ footerLine(item.app, item.card) }}</p>
            <div class="pin__actions">
              <Button
                v-if="item.card && item.app.canUpdate"
                icon="pi pi-refresh" size="small" text severity="secondary" class="pin__icon"
                :class="{ 'pin__icon--busy': store.refreshing.has(item.app.id) }"
                :disabled="store.refreshing.has(item.app.id)"
                :aria-label="`Refresh the card of ${item.app.name}`" :title="store.refreshing.has(item.app.id) ? 'Refreshing…' : 'Refresh card'" data-testid="refresh-card"
                @click="refreshCard(item.app.id)"
              />
              <Button icon="pi pi-arrow-left" size="small" text severity="secondary" class="pin__icon" :disabled="index === 0" :aria-label="`Move ${item.app.name} earlier`" @click="shift(index, -1)" />
              <Button icon="pi pi-arrow-right" size="small" text severity="secondary" class="pin__icon" :disabled="index === store.pinned.length - 1" :aria-label="`Move ${item.app.name} later`" @click="shift(index, 1)" />
              <Button
                :icon="item.wide ? 'pi pi-window-minimize' : 'pi pi-window-maximize'" size="small" text severity="secondary" class="pin__icon pin__wide"
                :aria-label="item.wide ? `Make ${item.app.name} normal width` : `Make ${item.app.name} wide`" :title="item.wide ? 'Normal width' : 'Wide'"
                data-testid="toggle-wide" @click="toggleWide(item.app.id, !item.wide)"
              />
              <Button icon="pi pi-bookmark-fill" size="small" text severity="secondary" class="pin__icon" :aria-label="`Unpin ${item.app.name}`" title="Unpin" data-testid="unpin" @click="store.unpin(item.app.id)" />
            </div>
          </article>
        </div>
      </section>

      <section v-if="empty" class="home-empty" data-testid="home-empty">
        <p class="home-empty__title">Nothing needs you</p>
        <p class="home-empty__line">
          Pin the apps you care about to see how they are doing here.
          <template v-if="isAdmin">Install Server Console from the catalog to see this server's health too.</template>
        </p>
        <div class="home-empty__buttons">
          <Button label="Pick apps to pin" class="btn-ink" @click="router.push('/apps')" />
          <Button v-if="isAdmin" label="Open the catalog" outlined class="btn-outline" @click="router.push('/apps/catalog')" />
        </div>
      </section>
      <p v-else-if="store.pinned.length === 0" class="home-hint">
        Pin apps from the <RouterLink to="/apps">Apps list</RouterLink> or an app's page to see them here.
      </p>
    </template>
  </div>
</template>

<style scoped>
.home-view { max-width: 1400px; margin: 0 auto; display: flex; flex-direction: column; gap: 24px; }
.home-head__title { margin: 0; font-family: var(--display); font-size: 28px; font-weight: 700; color: var(--ink); }
.home-head__line { margin: 4px 0 0; color: var(--ink-soft); font-size: 15px; }
.home-section { margin: 0 0 10px; font-family: var(--display); font-size: 18px; font-weight: 600; color: var(--ink); }
.home-section-row { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 10px; }
.home-section-row .home-section { margin: 0; }
.view-switch { display: flex; gap: 2px; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
.pin-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
.pin-row { display: flex; align-items: center; gap: 12px; padding: 10px 14px; background: var(--paper); border: 1px solid var(--line); border-radius: 12px; color: inherit; text-decoration: none; }
.pin-row:hover { border-color: var(--signal); }
.pin-row__summary { flex: 1; min-width: 0; text-align: right; color: var(--ink-soft); font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.home-cards--mosaic { display: block; position: relative; }
.pin--wide { grid-column: span 2; }
.home-skeleton, .home-cards { display: flex; flex-direction: column; gap: 8px; }
.home-cards--apps { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 12px; align-items: start; }
.need {
  display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px;
  padding: 14px 16px; background: var(--paper); border: 1px solid var(--line); border-radius: 12px;
}
.need--critical { border-color: var(--danger, #c0392b); }
.need--warning { border-color: var(--warn, #d68910); }
.need__text { min-width: 0; }
.need__title { margin: 0; font-weight: 600; font-size: 15px; color: var(--ink); }
.need__detail { margin: 2px 0 0; color: var(--ink-soft); font-size: 13px; }
.need__requests { list-style: none; margin: 6px 0 0; padding: 0; }
.need__requests li + li { border-top: 1px solid var(--row-line); }
.need__actions { display: flex; align-items: center; gap: 4px; }
.pin { display: flex; flex-direction: column; gap: 10px; padding: 14px; background: var(--paper); border: 1px solid var(--line); border-radius: 12px; box-sizing: border-box; }
.pin:hover { border-color: var(--ink-soft); }
.pin--attention, .pin--attention:hover { border-color: var(--signal); }
.pin--failing, .pin--failing:hover { border-color: var(--hc-critical, #c0392b); }
.pin__link { display: flex; align-items: center; gap: 12px; color: inherit; text-decoration: none; cursor: grab; user-select: none; touch-action: none; }
.pin__link:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; border-radius: 8px; }
.pin__name { flex: 1; min-width: 0; font-weight: 600; font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pin__state { flex: none; display: inline-flex; align-items: center; gap: 6px; font-size: 13px; color: var(--ink-soft); }
.pin__dot { width: 8px; height: 8px; border-radius: 50%; background: var(--ink-soft); opacity: 0.7; }
.pin__state--attention { color: var(--signal-ink); }
.pin__state--attention .pin__dot { background: var(--signal); opacity: 1; }
.pin__state--failing { color: var(--hc-critical, #c0392b); }
.pin__state--failing .pin__dot { background: var(--hc-critical, #c0392b); opacity: 1; }
.pin__footer { margin: 0; font-size: 12px; color: var(--ink-soft); }
.pin__actions { display: flex; justify-content: flex-end; gap: 2px; }
.pin__icon--busy { opacity: 0.5; }
.pin--dragging { z-index: 20; transform: rotate(1.5deg) scale(1.02); box-shadow: 0 18px 40px rgb(0 0 0 / 0.22); cursor: grabbing; pointer-events: none; transition: transform 160ms ease-out, box-shadow 160ms ease-out; }
.pin-slot { box-sizing: border-box; border: 2px dashed var(--signal); border-radius: 12px; background: color-mix(in srgb, var(--signal) 8%, transparent); }
.pin, .pin-slot { transition: top 260ms ease-out, left 260ms ease-out, width 260ms ease-out, transform 220ms ease-out, box-shadow 220ms ease-out; }
.pin--dragging { transition: transform 160ms ease-out, box-shadow 160ms ease-out; }
.pin--settling { z-index: 10; transition: top 220ms ease-out, left 220ms ease-out, width 220ms ease-out, transform 220ms ease-out, box-shadow 220ms ease-out; }
@media (prefers-reduced-motion: reduce) {
  .pin, .pin-slot, .pin--dragging, .pin--settling { transition: none; }
  .pin--dragging { transform: none; }
}
@media (max-width: 559px) {
  .pin__wide { display: none; }
  .pin--wide { grid-column: auto; }
  .pin__link { cursor: auto; touch-action: auto; }
  .pin__actions .p-button { min-width: 44px; min-height: 44px; }
  .home-cards--apps { grid-template-columns: minmax(0, 1fr); }
}
.home-empty { display: flex; flex-direction: column; align-items: center; text-align: center; gap: 8px; padding: 40px 16px; background: var(--paper); border: 1px solid var(--line); border-radius: 18px; }
.home-empty__title { margin: 0; font-family: var(--display); font-size: 20px; font-weight: 600; }
.home-empty__line { margin: 0 0 8px; color: var(--ink-soft); max-width: 52ch; }
.home-empty__buttons { display: flex; flex-wrap: wrap; gap: 8px; justify-content: center; }
.home-hint { margin: 0; color: var(--ink-soft); font-size: 14px; }
.btn-ink.p-button { background: var(--ink); border-color: var(--ink); color: var(--paper); border-radius: 999px; font-weight: 600; }
.btn-ink.p-button:hover { background: var(--ink); border-color: var(--signal); color: var(--paper); }
.btn-outline.p-button { background: transparent; border: 1px solid var(--line); color: var(--ink); border-radius: 999px; font-weight: 600; }
</style>
