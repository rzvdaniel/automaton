<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import ToggleSwitch from 'primevue/toggleswitch'
import { useToast } from 'primevue/usetoast'
import { actionWebhooksService, type ActionWebhook } from '../services/action-webhooks.service'
import { appActionErrorMessage } from '../utils/app-action-error'
import { formatWhen } from '../utils/app-overview'

const props = defineProps<{
  appId: string
  actionKey: string
  webhooks: ReadonlyArray<ActionWebhook>
  canUpdate: boolean
}>()
const emit = defineEmits<{ changed: [] }>()

const toast = useToast()
const adding = ref(false)
const name = ref('')
const busy = ref(false)
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
  name.value = ''
  adding.value = false
}, 'The webhook was not added')

const rotate = (hook: ActionWebhook) => attempt(async () => {
  revealed.value = await actionWebhooksService.rotate(props.appId, hook.id)
}, 'The secret was not rotated')

const setEnabled = (hook: ActionWebhook, enabled: boolean) => attempt(async () => {
  await actionWebhooksService.setEnabled(props.appId, hook.id, enabled)
}, 'The webhook was not changed')

const remove = (hook: ActionWebhook) => attempt(async () => {
  await actionWebhooksService.remove(props.appId, hook.id)
  if (revealed.value?.id === hook.id) revealed.value = null
}, 'The webhook was not removed')

async function copy(text: string) {
  try { await navigator.clipboard.writeText(text) } catch { /* the text is on screen to copy by hand */ }
}

const curl = (hook: ActionWebhook) =>
  `curl -X POST ${hook.url} -H "Authorization: Bearer ${hook.secret}" -H "Content-Type: application/json" -d '{}'`
</script>

<template>
  <div class="webhooks" data-testid="webhooks">
    <div v-for="hook in webhooks" :key="hook.id" class="hook" data-testid="webhook-row">
      <div class="line">
        <div class="hint">
          <strong data-testid="webhook-name">{{ hook.name }}</strong> · webhook ·
          <span class="mono">{{ hook.url }}</span>
          <span v-if="hook.calls.length"> · last call {{ hook.calls[0]!.outcome.toLowerCase() }} {{ formatWhen(hook.calls[0]!.calledAtUtc) }}</span>
          <span v-else> · not called yet</span>
        </div>
        <div v-if="canUpdate" class="controls">
          <ToggleSwitch :model-value="hook.enabled" :disabled="busy" :aria-label="`Webhook ${hook.name}`" data-testid="webhook-switch" @update:model-value="setEnabled(hook, $event)" />
          <Button label="Rotate secret" size="small" severity="secondary" text :disabled="busy" data-testid="webhook-rotate" @click="rotate(hook)" />
          <Button label="Remove" size="small" severity="secondary" text :disabled="busy" data-testid="webhook-remove" @click="remove(hook)" />
        </div>
      </div>
      <ul v-if="hook.calls.length" class="calls hint" data-testid="webhook-calls">
        <li v-for="call in hook.calls.slice(0, 5)" :key="call.calledAtUtc + call.outcome">{{ formatWhen(call.calledAtUtc) }} · {{ call.outcome }}<span v-if="call.detail && call.outcome !== 'Started'"> · {{ call.detail }}</span></li>
      </ul>
      <div v-if="revealed && revealed.id === hook.id" class="secret" data-testid="webhook-secret">
        <p class="hint">Copy the secret now. It is shown only once; rotate it if you lose it.</p>
        <code class="mono block">{{ revealed.secret }}</code>
        <code class="mono block">{{ curl(revealed) }}</code>
        <Button label="Copy secret" size="small" severity="secondary" outlined @click="copy(revealed.secret ?? '')" />
        <Button label="Done" size="small" text @click="revealed = null" />
      </div>
    </div>
    <div v-if="canUpdate" class="add">
      <template v-if="adding">
        <InputText v-model="name" placeholder="Webhook name" maxlength="160" size="small" data-testid="webhook-name-input" @keydown.enter.prevent="name.trim() && create()" />
        <Button label="Create" size="small" :disabled="busy || !name.trim()" data-testid="webhook-create" @click="create" />
        <Button label="Cancel" size="small" severity="secondary" text @click="adding = false" />
      </template>
      <Button v-else label="Add webhook" icon="pi pi-plus" size="small" severity="secondary" text data-testid="webhook-add" @click="adding = true" />
    </div>
  </div>
</template>

<style scoped>
.webhooks { display: flex; flex-direction: column; gap: 6px; margin-top: 6px; }
.line { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
.controls, .add { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.mono { font-family: var(--mono); font-size: 12px; }
.block { display: block; margin: 6px 0; overflow-wrap: anywhere; }
.calls { margin: 2px 0 0; padding-left: 18px; }
.secret { padding: 8px 12px; border: 1px solid var(--line); border-radius: var(--radius-card); }
</style>
