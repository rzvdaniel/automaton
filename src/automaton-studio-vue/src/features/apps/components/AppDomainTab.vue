<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import { appsService } from '../services/apps.service'
import type { AppCustomDomain, AppCustomDomains, DomainCheckStep } from '../types/apps.types'

const props = defineProps<{ appId: string; appName: string; isPublic: boolean; publicHost: string }>()
const emit = defineEmits<{ share: [] }>()

interface Check { phase: 'checking' | 'done' | 'failed'; steps: DomainCheckStep[]; message: string }

const state = ref<AppCustomDomains | null>(null)
const host = ref('')
const busy = ref(false)
const error = ref('')
const checks = ref<Record<string, Check>>({})
const copied = ref('')

function messageOf(caught: unknown, fallback = 'Could not change the domain. Try again.') {
  return (caught as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

async function run(action: () => Promise<AppCustomDomains>) {
  busy.value = true
  error.value = ''
  try {
    state.value = await action()
    return true
  } catch (caught) {
    error.value = messageOf(caught)
    return false
  } finally {
    busy.value = false
  }
}

function load() {
  if (props.isPublic) void run(() => appsService.getCustomDomains(props.appId))
}
onMounted(load)
watch(() => [props.appId, props.isPublic], load)

async function add() {
  if (await run(() => appsService.addCustomDomain(props.appId, host.value))) host.value = ''
}

const PENDING: DomainCheckStep[] = [
  { key: 'txt', title: 'Ownership (TXT record)', status: 'skipped', detail: 'Looking up the record' },
  { key: 'points', title: 'Points to this server', status: 'skipped', detail: 'Waiting' },
  { key: 'certificate', title: 'Certificate', status: 'skipped', detail: 'Waiting' },
]

async function check(domain: AppCustomDomain) {
  checks.value[domain.id] = { phase: 'checking', steps: PENDING, message: '' }
  try {
    const result = await appsService.verifyCustomDomain(props.appId, domain.id)
    state.value = result
    checks.value[domain.id] = { phase: 'done', steps: result.check ?? [], message: '' }
  } catch (caught) {
    const data = (caught as { response?: { data?: { message?: string; steps?: DomainCheckStep[] } } })?.response?.data
    checks.value[domain.id] = { phase: 'failed', steps: data?.steps ?? [], message: data?.message ?? 'The check could not be run. Try again.' }
  }
}

/** The zone is taken as the last two labels; most DNS providers add it to a relative name themselves. */
function zoneOf(domainHost: string) {
  const labels = domainHost.split('.')
  return labels.slice(-2).join('.')
}

function relative(name: string, domainHost: string) {
  const zone = zoneOf(domainHost)
  return name === zone ? '@' : name.endsWith('.' + zone) ? name.slice(0, -zone.length - 1) : name
}

function records(domain: AppCustomDomain) {
  const rows = [{ key: 't', type: 'TXT', name: relative(domain.challengeHost, domain.host), value: domain.challengeValue }]
  const target = state.value?.target ?? ''
  const name = relative(domain.host, domain.host)
  if (name === '@') {
    // A CNAME is not allowed on the root of a zone, so the A record is the only way.
    for (const address of state.value?.targetAddresses ?? []) rows.push({ key: 'a' + address, type: 'A', name, value: address })
  } else {
    rows.push({ key: 'c', type: 'CNAME', name, value: target })
  }
  return rows
}

async function copy(key: string, value: string) {
  try { await navigator.clipboard.writeText(value) } catch { /* the value stays visible to copy by hand */ }
  copied.value = key
  setTimeout(() => { if (copied.value === key) copied.value = '' }, 1200)
}

function chip(domain: AppCustomDomain): { label: string; severity: 'success' | 'warn' | 'secondary' } {
  if (checks.value[domain.id]?.phase === 'checking') return { label: 'Checking…', severity: 'secondary' }
  if (domain.live) return { label: domain.isPrimary ? 'Live · primary address' : 'Live · redirects to the primary', severity: 'success' }
  if (domain.verified) return { label: 'Verified · waiting for the certificate', severity: 'warn' }
  return checks.value[domain.id]?.phase === 'failed' ? { label: 'Not pointing here yet', severity: 'warn' } : { label: 'Waiting for DNS', severity: 'warn' }
}

const stepIcon = (status: DomainCheckStep['status']) => status === 'ok' ? 'pi pi-check-circle' : status === 'failed' ? 'pi pi-times-circle' : 'pi pi-minus-circle'
const aRecord = computed(() => state.value?.targetAddresses?.[0] ?? '')
</script>

<template>
  <section class="card" data-testid="domain-tab">
    <div class="head">
      <h2>Your own domain</h2>
      <p v-if="isPublic" class="hint">Serve {{ appName }} on a domain you own. Once it is live, <code>{{ publicHost }}</code> redirects to it.</p>
    </div>

    <div v-if="!isPublic" class="empty" data-testid="domain-not-public">
      <p class="hint">A domain of your own is for apps open to anyone on the internet. Make {{ appName }} public first, then come back here.</p>
      <Button label="Share…" icon="pi pi-share-alt" size="small" @click="emit('share')" />
    </div>

    <template v-else>
      <article v-for="domain in state?.domains ?? []" :key="domain.id" class="domain" data-testid="custom-domain">
        <div class="domain-head">
          <strong class="mono">{{ domain.host }}</strong>
          <Tag :value="chip(domain).label" :severity="chip(domain).severity" rounded />
          <span class="spacer"></span>
          <Button v-if="domain.live && !domain.isPrimary" label="Make primary" size="small" text :disabled="busy" @click="run(() => appsService.setPrimaryCustomDomain(appId, domain.id))" />
          <Button label="Remove" size="small" severity="danger" text :disabled="busy" data-testid="custom-domain-remove" @click="run(() => appsService.removeCustomDomain(appId, domain.id))" />
        </div>

        <template v-if="!domain.verified">
          <h3>1. Add these records at your DNS provider</h3>
          <div class="table-wrap">
            <table data-testid="dns-records">
              <thead><tr><th>Type</th><th>Name</th><th>Value</th></tr></thead>
              <tbody>
                <tr v-for="row in records(domain)" :key="row.key">
                  <td>{{ row.type }}</td>
                  <td><span class="cell"><code>{{ row.name }}</code><Button :label="copied === row.key + 'n' ? 'Copied' : 'Copy'" size="small" text severity="secondary" :aria-label="`Copy the ${row.type} name`" @click="copy(row.key + 'n', row.name)" /></span></td>
                  <td><span class="cell"><code>{{ row.value }}</code><Button :label="copied === row.key + 'v' ? 'Copied' : 'Copy'" size="small" text severity="secondary" :aria-label="`Copy the ${row.type} value`" @click="copy(row.key + 'v', row.value)" /></span></td>
                </tr>
              </tbody>
            </table>
          </div>
          <ul class="notes hint">
            <li>Most providers add <code>{{ zoneOf(domain.host) }}</code> to the name for you. Full names: <code>{{ domain.challengeHost }}</code>, <code>{{ domain.host }}</code>.</li>
            <li><strong>Cloudflare:</strong> set the CNAME's proxy status to <em>DNS only</em> (grey cloud).</li>
            <li v-if="aRecord && relative(domain.host, domain.host) !== '@'">Can't use a CNAME? Add an <code>A</code> record for <code>{{ relative(domain.host, domain.host) }}</code> pointing to <code>{{ aRecord }}</code>.</li>
          </ul>

          <h3>2. Check DNS</h3>
          <div class="check-row">
            <Button label="Check DNS" :loading="checks[domain.id]?.phase === 'checking'" :disabled="busy" data-testid="custom-domain-verify" @click="check(domain)" />
            <span class="hint">DNS changes can take a few minutes to show up.</span>
          </div>
        </template>
        <div v-else class="check-row">
          <Button label="Check again" size="small" text :loading="checks[domain.id]?.phase === 'checking'" @click="check(domain)" />
        </div>

        <ul v-if="checks[domain.id]?.steps.length" class="steps" data-testid="check-steps">
          <li v-for="step in checks[domain.id]!.steps" :key="step.key" :class="`step step--${checks[domain.id]!.phase === 'checking' ? 'wait' : step.status}`">
            <i :class="checks[domain.id]!.phase === 'checking' ? 'pi pi-spin pi-spinner' : stepIcon(step.status)" aria-hidden="true"></i>
            <span><strong>{{ step.title }}</strong><br><span class="hint">{{ step.detail }}</span></span>
          </li>
        </ul>
        <Message v-if="checks[domain.id]?.phase === 'done' && domain.live" severity="success" :closable="false" data-testid="check-live">
          <strong>{{ domain.host }} is live.</strong> <template v-if="domain.isPrimary">Visitors to {{ publicHost }} are now redirected here.</template>
        </Message>
        <Message v-else-if="checks[domain.id]?.phase === 'done'" severity="info" :closable="false" data-testid="check-waiting">
          <strong>DNS is right.</strong> The certificate is being issued; the domain goes live once the app answers there. Check again in a minute.
        </Message>
        <Message v-else-if="checks[domain.id]?.phase === 'failed'" severity="warn" :closable="false" data-testid="check-failed">
          <strong>Not there yet.</strong> {{ checks[domain.id]!.message }} The app keeps its current address until then.
        </Message>
      </article>

      <form class="add" @submit.prevent="add">
        <label for="domain-host">Add another domain</label>
        <div class="add-row">
          <InputText id="domain-host" v-model="host" placeholder="blog.example.com" data-testid="custom-domain-input" />
          <Button type="submit" label="Add" :disabled="busy || !host.trim()" data-testid="custom-domain-add" />
        </div>
      </form>
      <Message v-if="error" severity="error" :closable="false" data-testid="custom-domain-error">{{ error }}</Message>
    </template>
  </section>
</template>

<style scoped>
.card { display: flex; flex-direction: column; gap: 16px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
h3 { margin: 4px 0 0; font-size: 14px; font-weight: 600; }
.head p, .empty p { margin: 4px 0 0; }
.hint { color: var(--ink-soft); font-size: 13px; }
.mono, code { font-family: var(--mono); font-size: 12px; overflow-wrap: anywhere; }
.empty { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
.domain { display: flex; flex-direction: column; gap: 12px; padding: 16px; border: 1px solid var(--line); border-radius: var(--radius-card); }
.domain-head { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
.spacer { flex: 1; }
.table-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
th { padding: 6px 8px; text-align: left; color: var(--ink-soft); font-weight: 500; border-bottom: 1px solid var(--line); }
td { padding: 6px 8px; border-bottom: 1px solid var(--row-line); vertical-align: middle; }
.cell { display: flex; align-items: center; gap: 6px; }
.notes { margin: 0; padding-left: 18px; display: flex; flex-direction: column; gap: 4px; }
.check-row { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
.steps { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
.step { display: flex; align-items: flex-start; gap: 10px; }
.step i { font-size: 18px; margin-top: 2px; }
.step--ok i { color: var(--ok); }
.step--failed i { color: var(--signal-ink); }
.step--wait i, .step--skipped i { color: var(--ink-soft); }
.add { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
.add-row { display: flex; gap: 8px; }
.add-row :deep(input) { flex: 1; min-width: 0; }
</style>
