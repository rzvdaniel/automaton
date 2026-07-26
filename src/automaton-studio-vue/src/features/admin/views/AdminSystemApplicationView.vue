<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import { adminSystemApplicationService, type SystemApplicationResponse } from '../services/adminSystemApplicationService'

const data = ref<SystemApplicationResponse>()
const activeTab = ref<'application' | 'actions' | 'schedules' | 'executions'>('application')
const error = ref('')
const loading = ref(true)
const tabs = [
  ['application', 'Application'],
  ['actions', 'Actions'],
  ['schedules', 'Schedules'],
  ['executions', 'Executions'],
] as const

function date(value?: string) {
  return value ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—'
}

async function load() {
  loading.value = true
  error.value = ''
  try { data.value = await adminSystemApplicationService.get() }
  catch (reason: any) { error.value = reason?.response?.data?.message || 'System Application information could not be loaded.' }
  finally { loading.value = false }
}

onMounted(load)
</script>

<template>
  <div class="system-page">
    <header><div><h1>System Application</h1><p>Reviewed platform Actions and their scheduling history. Action code is read-only.</p></div><Button icon="pi pi-refresh" text rounded aria-label="Refresh" @click="load" /></header>
    <nav aria-label="System Application sections">
      <Button v-for="[key,label] in tabs" :key="key" :label="label" :text="activeTab !== key" @click="activeTab = key" />
    </nav>
    <div v-if="loading" class="state">Loading System Application…</div>
    <div v-else-if="error" class="state error">{{ error }} <Button label="Retry" text @click="load" /></div>
    <template v-else-if="data">
      <Card v-if="activeTab === 'application'">
        <template #title>{{ data.application.name }}</template>
        <template #content><dl><dt>Application ID</dt><dd>{{ data.application.id }}</dd><dt>Access</dt><dd>Read-only platform source</dd><dt>Actions</dt><dd>{{ data.actions.length }}</dd><dt>Schedules</dt><dd>{{ data.schedules.length }}</dd></dl></template>
      </Card>
      <div v-if="activeTab === 'actions'" class="grid">
        <Card v-for="action in data.actions" :key="action.key">
          <template #title>{{ action.name || action.key }}</template><template #subtitle>{{ action.key }}</template>
          <template #content>
            <p>{{ action.description || 'No description.' }}</p>
            <dl><dt>Trust</dt><dd>{{ action.trustLevel }}</dd><dt>Scopes</dt><dd>{{ action.allowedExecutionScopes.join(', ') || 'None' }}</dd><dt>Schedulable</dt><dd>{{ action.schedulable ? 'Yes' : 'No' }}</dd><dt>Timeout</dt><dd>{{ action.timeoutSeconds }}s</dd><dt>Minimum interval</dt><dd>{{ action.minimumIntervalSeconds }}s</dd><dt>Last executed version</dt><dd>{{ action.lastExecutedHash || 'Not reported' }}</dd><dt>Schedules</dt><dd>{{ action.scheduleCount }}</dd><dt>Last execution</dt><dd>{{ action.lastExecution?.status || '—' }}</dd></dl>
            <details><summary>Input schema</summary><pre>{{ JSON.stringify(action.inputSchema || {}, null, 2) }}</pre></details>
          </template>
        </Card>
      </div>
      <div v-if="activeTab === 'schedules'">
        <div v-if="!data.schedules.length" class="state">No System schedules.</div>
        <div v-for="schedule in data.schedules" :key="schedule.id" class="row"><div><strong>{{ schedule.name }}</strong><span>{{ schedule.actionKey }}</span></div><span>{{ schedule.enabled ? 'Enabled' : 'Disabled' }}</span><span>Next: {{ date(schedule.nextRunAtUtc) }}</span></div>
      </div>
      <div v-if="activeTab === 'executions'">
        <div v-if="!data.executions.length" class="state">No System Action executions.</div>
        <div v-for="execution in data.executions" :key="execution.id" class="row"><div><strong>{{ execution.moduleKey }}</strong><span>{{ date(execution.createdAtUtc) }}</span><small v-if="execution.failureSummary">{{ execution.failureSummary }}</small></div><span>{{ execution.status }}</span></div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.system-page{height:100%;overflow:auto;padding:.25rem}header,.row{display:flex;align-items:center;justify-content:space-between;gap:1rem}header h1,header p{margin:.2rem 0}header p,.row span,small{color:var(--p-text-muted-color)}nav{display:flex;gap:.25rem;margin:1rem 0;overflow:auto}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:1rem}dl{display:grid;grid-template-columns:max-content 1fr;gap:.4rem 1rem}dt{font-weight:600}dd{margin:0;overflow-wrap:anywhere}.row{padding:1rem;border-bottom:1px solid var(--p-surface-200)}.row>div{display:flex;flex:1;flex-direction:column}.state{padding:2rem;text-align:center;color:var(--p-text-muted-color)}.error{color:var(--p-red-500)}pre{white-space:pre-wrap;overflow-wrap:anywhere}@media(max-width:700px){.row{align-items:flex-start;flex-direction:column}}
</style>
