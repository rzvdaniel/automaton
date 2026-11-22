<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import ToggleSwitch from 'primevue/toggleswitch'
import { useToast } from 'primevue/usetoast'
import { actionWebhooksService, type ActionWebhook } from '../services/action-webhooks.service'
import { appActionErrorMessage } from '../utils/app-action-error'
import { formatWhen } from '../utils/app-overview'

/** The webhook triggers of one Action: a row each, with its edit form, and the form that adds one. */
const props = defineProps<{
  appId: string
  actionKey: string
  actionName: string
  webhooks: ReadonlyArray<ActionWebhook>
  canUpdate: boolean
  adding: boolean
}>()
const emit = defineEmits<{ changed: []; 'stop-adding': [] }>()

const toast = useToast()
const name = ref('')
const busy = ref(false)
const openId = ref<string | null>(null)
const confirming = ref<{ id: string; what: 'delete' | 'rotate' } | null>(null)
/** The secret is shown once, right after it is made or rotated; it is not kept anywhere else. */
const revealed = ref<ActionWebhook | null>(null)

async function attempt(work: () => Promise<void>, failure: string) {
  busy.value = true
  try {
    await work()
    emit('changed')
  } catch (error) {
    toast.add({ severity: 'error', summary: failure, detail: appActionErrorMessage(error, 'Try again.'), life: 6000 })
  } finally {
    busy.value = false
  }
}

const create = () => attempt(async () => {
  revealed.value = await actionWebhooksService.create(props.appId, props.actionKey, name.value.trim())
  openId.value = revealed.value.id
  name.value = ''
  emit('stop-adding')
}, 'The webhook was not added')

const rotate = (hook: ActionWebhook) => attempt(async () => {
  revealed.value = await actionWebhooksService.rotate(props.appId, hook.id)
  confirming.value = null
}, 'The secret was not rotated')

const setEnabled = (hook: ActionWebhook, enabled: boolean) => attempt(async () => {
  await actionWebhooksService.setEnabled(props.appId, hook.id, enabled)
}, 'The webhook was not changed')

const remove = (hook: ActionWebhook) => attempt(async () => {
  await actionWebhooksService.remove(props.appId, hook.id)
  if (revealed.value?.id === hook.id) revealed.value = null
  openId.value = null
  confirming.value = null
}, 'The webhook was not removed')

async function copy(text: string) {
  try { await navigator.clipboard.writeText(text) } catch { /* the text is on screen to copy by hand */ }
}

const toggleOpen = (hook: ActionWebhook) => { openId.value = openId.value === hook.id ? null : hook.id; confirming.value = null }

const curl = (hook: ActionWebhook) =>
  `curl -X POST ${hook.url} -H "Authorization: Bearer ${hook.secret}" -H "Content-Type: application/json" -d '{}'`
</script>

<template>
  <div class="webhooks" data-testid="webhooks">
    <div v-for="hook in webhooks" :key="hook.id" class="trigger" data-testid="webhook-row">
      <div class="line">
        <span class="badge hook">Webhook</span>
        <span class="summary">
          <span class="mono" data-testid="webhook-name">{{ hook.name }}</span>
          <span class="hint">· <span class="mono">{{ hook.url }}</span></span>
          <span v-if="hook.calls.length" class="hint">· last call {{ hook.calls[0]!.outcome.toLowerCase() }} {{ formatWhen(hook.calls[0]!.calledAtUtc) }}</span>
          <span v-else class="hint">· not called yet</span>
        </span>
        <ToggleSwitch v-if="canUpdate" :model-value="hook.enabled" :disabled="busy" :aria-label="`${actionName} webhook ${hook.enabled ? 'on' : 'off'}`" data-testid="webhook-switch" @update:model-value="setEnabled(hook, $event)" />
        <Button v-if="canUpdate" :label="openId === hook.id ? 'Close' : 'Edit'" size="small" severity="secondary" outlined :aria-expanded="openId === hook.id" data-testid="webhook-edit" @click="toggleOpen(hook)" />
      </div>
      <div v-if="openId === hook.id" class="form" data-testid="webhook-form">
        <div class="field wide">
          <span class="label">Address</span>
          <div class="copyline">
            <InputText :model-value="hook.url" readonly class="mono grow" aria-label="Webhook address" />
            <Button label="Copy" size="small" severity="secondary" outlined data-testid="webhook-copy" @click="copy(hook.url)" />
          </div>
        </div>
        <div class="field">
          <span class="label">Secret</span>
          <p class="hint">Shown once when created. Callers send it as a bearer token (<code>Authorization: Bearer …</code>).</p>
          <template v-if="confirming?.id === hook.id && confirming.what === 'rotate'">
            <p class="hint">Callers using the old secret stop working. Make a new one?</p>
            <div class="inline">
              <Button label="New secret" size="small" :disabled="busy" data-testid="webhook-rotate-confirm" @click="rotate(hook)" />
              <Button label="Keep the old one" size="small" severity="secondary" text @click="confirming = null" />
            </div>
          </template>
          <Button v-else label="New secret…" size="small" severity="secondary" outlined class="self" :disabled="busy" data-testid="webhook-rotate" @click="confirming = { id: hook.id, what: 'rotate' }" />
        </div>
        <div class="field">
          <span class="label">Recent calls</span>
          <ul v-if="hook.calls.length" class="calls" data-testid="webhook-calls">
            <li v-for="call in hook.calls.slice(0, 5)" :key="call.calledAtUtc + call.outcome">{{ formatWhen(call.calledAtUtc) }} · {{ call.outcome }}<span v-if="call.detail && call.outcome !== 'Started'"> · {{ call.detail }}</span></li>
          </ul>
          <p v-else class="hint">None yet.</p>
        </div>
        <div v-if="revealed && revealed.id === hook.id" class="secret wide" data-testid="webhook-secret">
          <p class="hint">Copy the secret now. It is shown only once; make a new one if you lose it.</p>
          <code class="mono block">{{ revealed.secret }}</code>
          <code class="mono block">{{ curl(revealed) }}</code>
          <Button label="Copy secret" size="small" severity="secondary" outlined @click="copy(revealed.secret ?? '')" />
          <Button label="Done" size="small" text @click="revealed = null" />
        </div>
        <div class="footer wide">
          <template v-if="confirming?.id === hook.id && confirming.what === 'delete'">
            <span>Delete this webhook?</span>
            <Button label="Delete" size="small" severity="danger" :disabled="busy" data-testid="webhook-remove-confirm" @click="remove(hook)" />
            <Button label="Keep it" size="small" severity="secondary" text @click="confirming = null" />
          </template>
          <Button v-else label="Delete…" size="small" severity="danger" text :disabled="busy" data-testid="webhook-remove" @click="confirming = { id: hook.id, what: 'delete' }" />
          <div class="grow" />
          <Button label="Close" size="small" severity="secondary" outlined @click="openId = null" />
        </div>
      </div>
    </div>
    <div v-if="canUpdate && adding" class="add" data-testid="webhook-add-form">
      <InputText v-model="name" placeholder="Webhook name" maxlength="160" size="small" aria-label="Webhook name" data-testid="webhook-name-input" @keydown.enter.prevent="name.trim() && create()" />
      <Button label="Create" size="small" :disabled="busy || !name.trim()" data-testid="webhook-create" @click="create" />
      <Button label="Cancel" size="small" severity="secondary" text @click="emit('stop-adding')" />
    </div>
  </div>
</template>

<style scoped>
.webhooks { display: flex; flex-direction: column; }
.trigger { border-top: 1px solid var(--row-line); }
.line { display: flex; align-items: center; gap: 12px; padding: 6px 4px; min-height: 44px; }
.line :deep(.p-toggleswitch) { flex-shrink: 0; }
.summary { flex-grow: 1; display: flex; flex-wrap: wrap; align-items: baseline; gap: 8px; min-width: 0; overflow-wrap: anywhere; }
.badge { font-size: 12px; font-weight: 600; border-radius: 6px; padding: 2px 8px; min-width: 64px; text-align: center; }
.hook { background: color-mix(in srgb, var(--ok) 14%, var(--paper)); color: var(--ok); }
.form { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 16px 28px; padding: 16px; margin: 4px 0 10px; border: 1px solid var(--line); border-radius: 10px; background: var(--panel); }
.field { display: flex; flex-direction: column; gap: 6px; }
.wide { grid-column: 1 / -1; }
.label { font-weight: 500; }
.copyline, .inline, .add { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.add { padding: 6px 4px; }
.footer { display: flex; align-items: center; gap: 12px; }
.grow { flex-grow: 1; }
.self { align-self: flex-start; }
.calls { margin: 0; padding: 0; list-style: none; font-size: 13px; display: flex; flex-direction: column; gap: 4px; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.mono { font-family: var(--mono); font-size: 13px; }
.block { display: block; margin: 6px 0; overflow-wrap: anywhere; }
.secret { padding: 8px 12px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
</style>
