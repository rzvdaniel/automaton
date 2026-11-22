<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import { useToast } from 'primevue/usetoast'
import { appsService } from '../services/apps.service'
import type { App, AppScreenshot, AppSharing } from '../types/apps.types'
import { resolveMediaUrl } from '../utils/media-url'
import AppMark from './AppMark.vue'

const props = defineProps<{ app: App; sharing: AppSharing | null }>()
const emit = defineEmits<{ changed: []; share: [] }>()
const toast = useToast()

const IMAGE_TYPES = 'image/png,image/jpeg,image/webp,image/gif'
const isPublic = computed(() => props.sharing?.audience === 'internet')
const listed = ref(false)
const summary = ref('')
const category = ref('')
const tags = ref('')
const categories = ref<string[]>([])
const cover = ref<string | null>(null)
const screenshots = ref<AppScreenshot[]>([])
const saved = ref('')
const busy = ref(false)
const error = ref('')
const dragFrom = ref<number | null>(null)

const text = computed(() => JSON.stringify([summary.value.trim(), category.value]))
const changed = computed(() => text.value !== saved.value)

function messageOf(reason: unknown, fallback: string) {
  return (reason as { response?: { data?: { message?: string } } })?.response?.data?.message ?? (reason instanceof Error ? reason.message : fallback)
}

async function load() {
  error.value = ''
  try {
    const current = await appsService.get(props.app.id)
    summary.value = current.summary ?? ''
    category.value = current.category ?? ''
    tags.value = current.tags ?? ''
    cover.value = current.thumbnailUrl ?? null
    screenshots.value = [...(current.screenshots ?? [])].sort((a, b) => a.sortOrder - b.sortOrder)
    categories.value = await appsService.getMarketplaceCategories()
  } catch (reason) {
    error.value = messageOf(reason, 'The store listing could not be loaded.')
  }
  saved.value = text.value
}

onMounted(load)
watch(() => props.app.id, load)
watch(() => props.sharing, (state) => { listed.value = state?.listed ?? false }, { immediate: true })

async function attempt(action: () => Promise<void>, failure: string) {
  busy.value = true
  error.value = ''
  try { await action() } catch (reason) { error.value = messageOf(reason, failure) } finally { busy.value = false }
}

const toggleListed = () => attempt(async () => {
  const next = !listed.value
  await appsService.setSharing(props.app.id, 'internet', props.sharing?.publicUrlSlug ?? undefined, undefined, next)
  listed.value = next
  emit('changed')
}, 'The listing could not be changed.')

const save = () => attempt(async () => {
  await appsService.updateListing(props.app.id, {
    summary: summary.value.trim(),
    category: category.value,
    tags: tags.value.split(',').map((tag) => tag.trim()).filter(Boolean),
  })
  saved.value = text.value
  toast.add({ severity: 'success', summary: 'Listing saved', life: 2500 })
}, 'The listing could not be saved.')

function picked(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  return file
}

async function onCover(event: Event) {
  const file = picked(event)
  if (file) await attempt(async () => { cover.value = await appsService.uploadThumbnail(props.app.id, file) }, 'That image could not be uploaded (PNG, JPG, WEBP or GIF up to 2 MB).')
}

const removeCover = () => attempt(async () => {
  await appsService.removeThumbnail(props.app.id)
  cover.value = null
}, 'The cover could not be removed.')

async function onScreenshot(event: Event) {
  const file = picked(event)
  if (file) await attempt(async () => { screenshots.value = [...screenshots.value, await appsService.uploadScreenshot(props.app.id, file)] }, 'That image could not be uploaded (PNG, JPG, WEBP or GIF up to 2 MB).')
}

const removeScreenshot = (id: string) => attempt(async () => {
  await appsService.deleteScreenshot(props.app.id, id)
  screenshots.value = screenshots.value.filter((shot) => shot.id !== id)
}, 'That screenshot could not be removed.')

/** Moves a screenshot and saves the new order; the old order returns if saving fails. */
async function move(from: number, to: number) {
  if (to < 0 || to >= screenshots.value.length || from === to) return
  const before = screenshots.value
  const next = [...before]
  next.splice(to, 0, ...next.splice(from, 1))
  screenshots.value = next
  await attempt(async () => {
    try { await appsService.reorderScreenshots(props.app.id, next.map((shot) => shot.id)) } catch (reason) { screenshots.value = before; throw reason }
  }, 'The order could not be saved.')
}

function drop(to: number) {
  if (dragFrom.value !== null) void move(dragFrom.value, to)
  dragFrom.value = null
}
</script>

<template>
  <div class="listing" data-testid="store-listing">
    <section class="card">
      <div class="head">
        <h2>Store listing</h2>
        <label class="toggle" data-testid="list-toggle">
          <input type="checkbox" :checked="listed" :disabled="busy || !isPublic" @change="toggleListed">
          <span>List in the store</span>
        </label>
      </div>
      <p v-if="!isPublic" class="hint" data-testid="listing-not-public">
        Only apps open to anyone on the internet can be listed.
        <a href="#" @click.prevent="emit('share')">Share…</a>
      </p>
      <Message v-if="error" severity="error" :closable="false" data-testid="listing-error">{{ error }}</Message>

      <label class="field">Summary
        <InputText v-model="summary" maxlength="200" placeholder="One line shown on the app card" data-testid="listing-summary" />
      </label>
      <label class="field">Category
        <select v-model="category" class="select" data-testid="listing-category">
          <option value="">No category</option>
          <option v-for="name in categories" :key="name" :value="name">{{ name }}</option>
        </select>
      </label>
      <label class="field">Tags
        <InputText v-model="tags" placeholder="game, multiplayer" />
      </label>
      <div class="actions"><Button label="Save" :disabled="!changed || busy" :loading="busy" data-testid="listing-save" @click="save" /></div>

      <div class="field">
        <span>Cover image</span>
        <div class="cover-row">
          <AppMark :id="app.id" :name="app.name" :image-url="cover" :size="64" />
          <div class="stack">
            <span class="buttons">
              <label class="upload">{{ cover ? 'Replace image' : 'Upload image' }}
                <input type="file" :accept="IMAGE_TYPES" hidden data-testid="listing-cover" @change="onCover">
              </label>
              <Button v-if="cover" label="Remove" size="small" text severity="secondary" @click="removeCover" />
            </span>
            <span class="hint">Generated automatically until you upload one. PNG, JPG, WEBP or GIF up to 2 MB.</span>
          </div>
        </div>
      </div>

      <div class="field">
        <span>Screenshots</span>
        <ul class="shots" data-testid="screenshots">
          <li v-for="(shot, index) in screenshots" :key="shot.id" class="shot" draggable="true" @dragstart="dragFrom = index" @dragover.prevent @drop.prevent="drop(index)">
            <img :src="resolveMediaUrl(shot.imageUrl)" :alt="`Screenshot ${index + 1}`">
            <span class="buttons">
              <Button icon="pi pi-arrow-left" text rounded size="small" severity="secondary" :disabled="index === 0 || busy" :aria-label="`Move screenshot ${index + 1} earlier`" @click="move(index, index - 1)" />
              <Button icon="pi pi-arrow-right" text rounded size="small" severity="secondary" :disabled="index === screenshots.length - 1 || busy" :aria-label="`Move screenshot ${index + 1} later`" @click="move(index, index + 1)" />
              <Button icon="pi pi-times" text rounded size="small" severity="secondary" :aria-label="`Remove screenshot ${index + 1}`" @click="removeScreenshot(shot.id)" />
            </span>
          </li>
          <li v-if="screenshots.length < 8" class="shot shot--add">
            <label class="upload">+ Add screenshot
              <input type="file" :accept="IMAGE_TYPES" hidden data-testid="listing-screenshot" @change="onScreenshot">
            </label>
          </li>
        </ul>
        <span class="hint">Drag to reorder. The CLI (<code>automaton listing</code>) works too.</span>
      </div>
    </section>

    <aside class="card preview" data-testid="listing-preview" aria-label="Preview in the store">
      <span class="hint">Preview in the store</span>
      <div class="store-card">
        <AppMark :id="app.id" :name="app.name" :image-url="cover" :size="48" />
        <div class="stack">
          <strong>{{ app.name }}</strong>
          <span class="hint">{{ summary || 'No summary yet' }}</span>
          <span v-if="category" class="chip">{{ category }}</span>
        </div>
      </div>
    </aside>
  </div>
</template>

<style scoped>
.listing { display: grid; grid-template-columns: minmax(0, 1fr) 280px; gap: 16px; align-items: start; }
@media (max-width: 800px) { .listing { grid-template-columns: 1fr; } }
.card { display: flex; flex-direction: column; gap: 14px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.toggle { display: inline-flex; align-items: center; gap: 8px; font-size: 14px; }
.hint { color: var(--ink-soft); font-size: 13px; }
p.hint { margin: 0; }
.field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
.select { padding: 8px 10px; border-radius: 8px; border: 1px solid var(--line); background: var(--paper); color: inherit; font: inherit; }
.actions { display: flex; justify-content: flex-end; }
.cover-row { display: flex; align-items: center; gap: 12px; }
.stack { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.buttons { display: inline-flex; align-items: center; gap: 4px; }
.upload { color: var(--signal-ink); cursor: pointer; text-decoration: underline; }
.shots { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 10px; }
.shot { display: flex; flex-direction: column; align-items: center; gap: 4px; padding: 6px; border: 1px solid var(--line); border-radius: 10px; cursor: grab; }
.shot img { height: 72px; max-width: 140px; border-radius: 6px; object-fit: cover; }
.shot--add { justify-content: center; min-width: 110px; min-height: 100px; border-style: dashed; cursor: default; }
.preview { gap: 10px; }
.store-card { display: flex; gap: 12px; }
.chip { align-self: flex-start; padding: 2px 10px; border-radius: 999px; background: var(--row-line); font-size: 12px; }
code { font-family: var(--mono); font-size: 12px; }
</style>
