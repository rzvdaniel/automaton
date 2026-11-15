<script setup lang="ts">
import { onMounted, ref } from 'vue'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import { appsService } from '../services/apps.service'
import type { AppCustomDomains } from '../types/apps.types'

const props = defineProps<{ appId: string }>()

const state = ref<AppCustomDomains | null>(null)
const host = ref('')
const busy = ref(false)
const error = ref('')

function message(caught: unknown): string {
  const response = (caught as { response?: { data?: { message?: string } } })?.response
  return response?.data?.message ?? 'Could not change the domain. Try again.'
}

async function run(action: () => Promise<AppCustomDomains>) {
  busy.value = true
  error.value = ''
  try {
    state.value = await action()
    return true
  } catch (caught) {
    error.value = message(caught)
    return false
  } finally {
    busy.value = false
  }
}

onMounted(() => run(() => appsService.getCustomDomains(props.appId)))

async function add() {
  if (await run(() => appsService.addCustomDomain(props.appId, host.value))) host.value = ''
}
</script>

<template>
  <section class="domains" data-testid="custom-domains">
    <h4 class="domains__title">Your own domain</h4>
    <p class="domains__hint">Add a domain you own, such as <code>blog.example.com</code>. The app then answers there, and its other address redirects to it.</p>

    <ul v-if="state?.domains.length" class="domains__list">
      <li v-for="domain in state.domains" :key="domain.id" class="domains__item" data-testid="custom-domain">
        <div>
          <strong>{{ domain.host }}</strong>
          <span v-if="domain.verified" class="domains__badge">{{ domain.isPrimary ? 'Primary address' : 'Redirects to the primary' }}</span>
          <span v-else class="domains__badge domains__badge--wait">Waiting for DNS</span>
        </div>
        <p v-if="!domain.verified" class="domains__hint" data-testid="custom-domain-dns">
          Create a <code>CNAME</code> record for <code>{{ domain.host }}</code> pointing to <code>{{ state.target }}</code>
          <template v-if="state.targetAddresses.length"> (or an <code>A</code> record to {{ state.targetAddresses.join(', ') }})</template>,
          then check it. A certificate is issued once DNS points here.
        </p>
        <div class="domains__actions">
          <Button v-if="!domain.verified" label="Check DNS" size="small" :disabled="busy" data-testid="custom-domain-verify"
                  @click="run(() => appsService.verifyCustomDomain(appId, domain.id))" />
          <Button v-else-if="!domain.isPrimary" label="Make primary" size="small" text :disabled="busy"
                  @click="run(() => appsService.setPrimaryCustomDomain(appId, domain.id))" />
          <Button label="Remove" size="small" severity="danger" text :disabled="busy" data-testid="custom-domain-remove"
                  @click="run(() => appsService.removeCustomDomain(appId, domain.id))" />
        </div>
      </li>
    </ul>

    <div class="domains__add">
      <InputText v-model="host" placeholder="blog.example.com" aria-label="Domain" data-testid="custom-domain-input" @keyup.enter="add" />
      <Button label="Add" :disabled="busy || !host.trim()" data-testid="custom-domain-add" @click="add" />
    </div>
    <p v-if="error" class="domains__error" role="alert" data-testid="custom-domain-error">{{ error }}</p>
  </section>
</template>

<style scoped>
.domains { display: flex; flex-direction: column; gap: 0.5rem; margin-top: 1rem; }
.domains__title { margin: 0; font-size: 0.95rem; }
.domains__hint { margin: 0; color: var(--p-text-muted-color); font-size: 0.85rem; }
.domains__list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.5rem; }
.domains__item { border: 1px solid var(--p-content-border-color); border-radius: 6px; padding: 0.5rem 0.75rem; }
.domains__badge { margin-left: 0.5rem; font-size: 0.75rem; color: var(--p-text-muted-color); }
.domains__badge--wait { color: var(--p-orange-500); }
.domains__actions { display: flex; gap: 0.25rem; margin-top: 0.25rem; }
.domains__add { display: flex; gap: 0.5rem; }
.domains__add :deep(input) { flex: 1; }
.domains__error { margin: 0; color: var(--p-red-500); font-size: 0.85rem; }
</style>
