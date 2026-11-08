<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { appsService } from '../services/apps.service'
import type { AppSharing, SharingAudience } from '../types/apps.types'
import { getPublishedAppUrl } from '../utils/public-app-url'

const props = defineProps<{
  visible: boolean
  appId: string
  appName: string
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  /** Who can open the app changed; the page reloads the app. */
  changed: []
}>()

const CHOICES: ReadonlyArray<{ key: SharingAudience, title: string, line: string, icon: string }> = [
  { key: 'me', title: 'Only me', line: 'Nobody else can open this app.', icon: 'pi pi-lock' },
  { key: 'people', title: 'People I choose', line: 'Only the people you add below can open it.', icon: 'pi pi-users' },
  { key: 'server', title: 'Everyone on this server', line: 'Anyone with an account here can open it after signing in.', icon: 'pi pi-building' },
  { key: 'internet', title: 'Anyone on the internet', line: 'No sign-in. The app gets a public address.', icon: 'pi pi-globe' },
]

const sharing = ref<AppSharing | null>(null)
const selected = ref<SharingAudience>('me')
const address = ref('')
const email = ref('')
const loading = ref(false)
const saving = ref(false)
const error = ref('')

const isVisible = computed({
  get: () => props.visible,
  set: (value) => emit('update:visible', value),
})

const addressValid = computed(() => /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/.test(address.value) && address.value.length >= 3)
const publicUrl = computed(() => getPublishedAppUrl(address.value || 'your-app'))
const unchanged = computed(() => !!sharing.value
  && sharing.value.audience === selected.value
  && (selected.value !== 'internet' || address.value === (sharing.value.publicUrlSlug ?? '')))
const canSave = computed(() => !saving.value && !unchanged.value
  && (selected.value !== 'internet' || addressValid.value)
  && (selected.value !== 'people' || (sharing.value?.people.length ?? 0) > 0))

function messageOf(reason: unknown, fallback: string): string {
  const data = (reason as { response?: { data?: { message?: string } } })?.response?.data
  return data?.message ?? (reason instanceof Error ? reason.message : fallback)
}

function apply(state: AppSharing) {
  sharing.value = state
  selected.value = state.audience
  address.value = state.publicUrlSlug ?? address.value
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    apply(await appsService.getSharing(props.appId))
    if (!address.value) address.value = appsService.generateSlug(props.appName)
  } catch (reason) {
    error.value = messageOf(reason, 'Could not load who can open this app.')
  } finally {
    loading.value = false
  }
}

watch(() => props.visible, (open) => {
  if (!open) return
  email.value = ''
  void load()
}, { immediate: true })

function onAddressInput(event: Event) {
  address.value = (event.target as HTMLInputElement).value
    .toLowerCase()
    .replace(/[^a-z0-9-]/g, '-')
    .replace(/-+/g, '-')
}

async function save() {
  saving.value = true
  error.value = ''
  try {
    apply(await appsService.setSharing(props.appId, selected.value, selected.value === 'internet' ? address.value : undefined))
    emit('changed')
  } catch (reason) {
    error.value = messageOf(reason, 'Sharing was not changed.')
  } finally {
    saving.value = false
  }
}

async function addPerson() {
  const value = email.value.trim()
  if (!value) return
  error.value = ''
  try {
    const wasPeople = selected.value === 'people'
    const state = await appsService.addSharedPerson(props.appId, value)
    email.value = ''
    // Adding someone while "People I choose" is selected is the whole action; other choices stay as they are.
    sharing.value = state
    if (wasPeople) selected.value = 'people'
    emit('changed')
  } catch (reason) {
    error.value = messageOf(reason, 'That person could not be added.')
  }
}

async function removePerson(userId: string) {
  error.value = ''
  try {
    sharing.value = await appsService.removeSharedPerson(props.appId, userId)
    emit('changed')
  } catch (reason) {
    error.value = messageOf(reason, 'That person could not be removed.')
  }
}

async function copyAddress() {
  try { await navigator.clipboard.writeText(publicUrl.value) } catch { /* the address stays visible to copy by hand */ }
}
</script>

<template>
  <Dialog
    v-model:visible="isVisible"
    :header="`Share ${appName}`"
    :modal="true"
    :style="{ width: '520px', maxWidth: '94vw' }"
  >
    <div class="share">
      <p class="share__lead">Who can open this app?</p>

      <Message v-if="error" severity="error" :closable="false" data-testid="share-error">{{ error }}</Message>

      <div role="radiogroup" aria-label="Who can open this app" class="share__choices">
        <label
          v-for="choice in CHOICES"
          :key="choice.key"
          class="choice"
          :class="{ 'choice--on': selected === choice.key }"
          :data-testid="`share-choice-${choice.key}`"
        >
          <input v-model="selected" type="radio" name="audience" :value="choice.key" :disabled="loading">
          <i :class="choice.icon" aria-hidden="true"></i>
          <span class="choice__text">
            <strong>{{ choice.title }}</strong>
            <span>{{ choice.line }}</span>
          </span>
        </label>
      </div>

      <section v-if="selected === 'people'" class="share__more" data-testid="share-people">
        <form class="share__add" @submit.prevent="addPerson">
          <InputText v-model="email" type="email" placeholder="Their email on this server" aria-label="Email of a person on this server" />
          <Button type="submit" label="Add" :disabled="!email.trim()" />
        </form>
        <ul v-if="sharing && sharing.people.length > 0" class="share__people">
          <li v-for="person in sharing.people" :key="person.userId">
            <span>{{ person.email }}</span>
            <Button
              icon="pi pi-times"
              text
              rounded
              severity="secondary"
              :aria-label="`Stop sharing with ${person.email}`"
              @click="removePerson(person.userId)"
            />
          </li>
        </ul>
        <p v-else class="share__hint">Nobody yet. They need an account on this server first.</p>
      </section>

      <section v-if="selected === 'internet'" class="share__more" data-testid="share-internet">
        <label for="share-address" class="share__label">Public address</label>
        <InputText id="share-address" :model-value="address" placeholder="my-app" @input="onAddressInput" />
        <p v-if="addressValid" class="share__hint">
          <code>{{ publicUrl }}</code>
          <Button label="Copy" icon="pi pi-copy" text size="small" @click="copyAddress" />
        </p>
        <p v-else class="share__hint">Use at least 3 lowercase letters, numbers or hyphens.</p>
        <p class="share__hint">Anyone with the address can open the app, so keep private data out of it.</p>
      </section>
    </div>

    <template #footer>
      <Button label="Close" severity="secondary" text @click="isVisible = false" />
      <Button label="Save" :loading="saving" :disabled="!canSave" data-testid="share-save" @click="save" />
    </template>
  </Dialog>
</template>

<style scoped>
.share { display: flex; flex-direction: column; gap: 12px; }
.share__lead { margin: 0; color: var(--p-text-muted-color, var(--p-text-color-secondary)); }
.share__choices { display: flex; flex-direction: column; gap: 8px; }
.choice {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  border: 1px solid var(--p-content-border-color, var(--p-surface-border));
  border-radius: 12px;
  cursor: pointer;
}
.choice--on { border-color: var(--p-primary-color); box-shadow: 0 0 0 1px var(--p-primary-color); }
.choice input { accent-color: var(--p-primary-color); }
.choice__text { display: flex; flex-direction: column; gap: 2px; font-size: 14px; }
.choice__text span { color: var(--p-text-muted-color, var(--p-text-color-secondary)); }
.share__more { display: flex; flex-direction: column; gap: 8px; padding: 4px 2px; }
.share__add { display: flex; gap: 8px; }
.share__add :deep(input) { flex: 1; }
.share__people { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
.share__people li { display: flex; align-items: center; justify-content: space-between; gap: 8px; font-size: 14px; }
.share__label { font-weight: 600; font-size: 14px; }
.share__hint { margin: 0; font-size: 13px; color: var(--p-text-muted-color, var(--p-text-color-secondary)); overflow-wrap: anywhere; }
</style>
