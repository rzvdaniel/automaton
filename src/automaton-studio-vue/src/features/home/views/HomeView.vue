<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import { useToast } from 'primevue/usetoast'
import AppMark from '@/features/apps/components/AppMark.vue'
import { getStudioApplicationUrl } from '@/features/apps/utils/public-app-url'
import { appState, formatShortTime } from '@/features/apps/utils/app-list-display'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { serverConsoleService } from '@/features/server/services/server-console.service'
import type { HomeCard } from '../types/home.types'
import { useHomeStore } from '../stores/home.store'
import { appFacts, moved } from '../utils'

const router = useRouter()
const store = useHomeStore()
const auth = useAuthStore()
const toast = useToast()

const isAdmin = computed(() => auth.user?.roles?.includes('SysAdmin') === true)
const empty = computed(() => store.pinned.length === 0 && store.needsYou.length === 0 && store.notifications.length === 0)

onMounted(() => store.load())

async function acknowledge(key: string) {
  try {
    await store.acknowledge(key)
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'The notification could not be acknowledged.', life: 5000 })
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

// Drag a card onto another to move it there.
const dragged = ref<number | null>(null)
async function drop(target: number) {
  const from = dragged.value
  dragged.value = null
  if (from === null || from === target) return
  const ids = moved(store.pinned.map((item) => item.app.id), from, target)
  try {
    await store.reorder(ids)
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'The order could not be saved.', life: 5000 })
  }
}
function shift(index: number, by: number) {
  dragged.value = index
  return drop(index + by)
}
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
            </div>
            <div class="need__actions">
              <Button
                :label="isStarting(card) ? 'Installing…' : isInstalled(card) ? 'Open' : card.actionLabel"
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
        <h2 id="pinned-title" class="home-section">Pinned apps</h2>
        <div class="home-cards home-cards--apps">
          <article
            v-for="(item, index) in store.pinned"
            :key="item.app.id"
            class="pin"
            draggable="true"
            data-testid="pinned-app"
            @dragstart="dragged = index"
            @dragover.prevent
            @drop.prevent="drop(index)"
          >
            <RouterLink :to="`/apps/${item.app.id}`" class="pin__link" :aria-label="`Open ${item.app.name}`">
              <AppMark :id="item.app.id" :name="item.app.name" :image-url="item.app.thumbnailUrl" />
              <span class="pin__name">{{ item.app.name }}</span>
              <span class="pin__state" :class="`pin__state--${appState(item.app).tone}`">{{ appState(item.app).label }}</span>
            </RouterLink>
            <div v-if="item.card" class="pin__card" :class="{ 'pin__card--stale': item.card.stale }" data-testid="app-card">
              <p class="pin__card-status" data-testid="app-card-status">{{ item.card.card.status }}</p>
              <dl v-if="item.card.card.facts.length > 0" class="pin__facts">
                <div v-for="fact in item.card.card.facts" :key="fact.label">
                  <dt>{{ fact.label }}</dt>
                  <dd>{{ fact.value }}</dd>
                </div>
              </dl>
              <ul v-if="item.card.card.alerts.length > 0" class="pin__alerts">
                <li v-for="(alert, alertIndex) in item.card.card.alerts" :key="alertIndex" :class="`pin__alert--${alert.severity}`" data-testid="app-card-alert">
                  <span class="pin__alert-level">{{ alert.severity }}</span> {{ alert.text }}
                </li>
              </ul>
              <p class="pin__card-time" data-testid="app-card-time">
                {{ item.card.stale ? 'Not up to date. Last update ' : 'Updated ' }}{{ formatShortTime(item.card.updatedAtUtc) }}
              </p>
            </div>
            <dl class="pin__facts">
              <div v-for="fact in appFacts(item.app)" :key="fact.label">
                <dt>{{ fact.label }}</dt>
                <dd>{{ fact.value }}</dd>
              </div>
            </dl>
            <div class="pin__actions">
              <Button icon="pi pi-arrow-left" size="small" text severity="secondary" :disabled="index === 0" :aria-label="`Move ${item.app.name} earlier`" @click="shift(index, -1)" />
              <Button icon="pi pi-arrow-right" size="small" text severity="secondary" :disabled="index === store.pinned.length - 1" :aria-label="`Move ${item.app.name} later`" @click="shift(index, 1)" />
              <Button icon="pi pi-bookmark-fill" size="small" text severity="secondary" :aria-label="`Unpin ${item.app.name}`" data-testid="unpin" @click="store.unpin(item.app.id)" />
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
.home-skeleton, .home-cards { display: flex; flex-direction: column; gap: 8px; }
.home-cards--apps { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 12px; }
.need {
  display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px;
  padding: 14px 16px; background: var(--paper); border: 1px solid var(--line); border-radius: 12px;
}
.need--critical { border-color: var(--danger, #c0392b); }
.need--warning { border-color: var(--warn, #d68910); }
.need__text { min-width: 0; }
.need__title { margin: 0; font-weight: 600; font-size: 15px; color: var(--ink); }
.need__detail { margin: 2px 0 0; color: var(--ink-soft); font-size: 13px; }
.need__actions { display: flex; align-items: center; gap: 4px; }
.pin { display: flex; flex-direction: column; gap: 10px; padding: 14px; background: var(--paper); border: 1px solid var(--line); border-radius: 12px; }
.pin:hover { border-color: var(--signal); }
.pin__link { display: flex; align-items: center; gap: 12px; color: inherit; text-decoration: none; }
.pin__link:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; border-radius: 8px; }
.pin__name { flex: 1; min-width: 0; font-weight: 600; font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pin__state { flex: none; padding: 3px 9px; border-radius: 999px; font-family: var(--mono); font-size: 11.5px; }
.pin__state--live { background: var(--signal); color: var(--on-signal); font-weight: 600; }
.pin__state--ok { border: 1px solid var(--ok); color: var(--ok); }
.pin__state--failing { background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.pin__state--stopped { background: var(--row-line); color: var(--ink-soft); }
.pin__facts { margin: 0; display: grid; gap: 4px; font-size: 13px; }
.pin__facts div { display: flex; justify-content: space-between; gap: 8px; }
.pin__facts dt { color: var(--ink-soft); }
.pin__facts dd { margin: 0; text-align: right; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pin__card { display: flex; flex-direction: column; gap: 6px; padding: 8px 10px; border-radius: 8px; background: var(--row-line); }
.pin__card--stale { opacity: 0.55; filter: grayscale(1); }
.pin__card-status { margin: 0; font-weight: 600; font-size: 14px; overflow-wrap: anywhere; }
.pin__alerts { margin: 0; padding: 0; list-style: none; display: grid; gap: 3px; font-size: 13px; overflow-wrap: anywhere; }
.pin__alert-level { font-family: var(--mono); font-size: 11px; text-transform: uppercase; }
.pin__alert--critical { color: var(--signal-ink); font-weight: 600; }
.pin__alert--warning { color: var(--ink); }
.pin__alert--info { color: var(--ink-soft); }
.pin__card-time { margin: 0; font-size: 12px; color: var(--ink-soft); }
.pin__actions { display: flex; justify-content: flex-end; gap: 2px; }
.home-empty { display: flex; flex-direction: column; align-items: center; text-align: center; gap: 8px; padding: 40px 16px; background: var(--paper); border: 1px solid var(--line); border-radius: 18px; }
.home-empty__title { margin: 0; font-family: var(--display); font-size: 20px; font-weight: 600; }
.home-empty__line { margin: 0 0 8px; color: var(--ink-soft); max-width: 52ch; }
.home-empty__buttons { display: flex; flex-wrap: wrap; gap: 8px; justify-content: center; }
.home-hint { margin: 0; color: var(--ink-soft); font-size: 14px; }
.btn-ink.p-button { background: var(--ink); border-color: var(--ink); color: var(--paper); border-radius: 999px; font-weight: 600; }
.btn-ink.p-button:hover { background: var(--ink); border-color: var(--signal); color: var(--paper); }
.btn-outline.p-button { background: transparent; border: 1px solid var(--line); color: var(--ink); border-radius: 999px; font-weight: 600; }
</style>
