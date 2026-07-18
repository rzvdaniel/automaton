<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import HostedRunnersTabs from '../components/HostedRunnersTabs.vue'
import { adminHostedRunnersService } from '../services/adminHostedRunnersService'
import type { HostedRunnerPlatformSettings, HostedRunnerResources, HostedRunnerSpecification, HostedRunnerSpecificationRequest } from '../types/admin.types'

const toast = useToast()
const specifications = ref<HostedRunnerSpecification[]>([])
const settings = ref<HostedRunnerPlatformSettings | null>(null)
const loading = ref(false)
const saving = ref(false)
const visible = ref(false)
const editing = ref<HostedRunnerSpecification | null>(null)

const defaultResources = (): HostedRunnerResources => ({ cpuLimit: 0.25, memoryLimitMb: 768, memoryReservationMb: 256, diskLimitMb: 2048, pidsLimit: 128, maximumApps: 3 })
const form = ref<HostedRunnerSpecificationRequest>({ name: '', code: '', description: '', isActive: true, resources: defaultResources() })

async function load() {
  loading.value = true
  try {
    ;[specifications.value, settings.value] = await Promise.all([
      adminHostedRunnersService.getSpecifications(),
      adminHostedRunnersService.getSpecificationSettings(),
    ])
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load hosted runner specifications', life: 3000 })
  } finally { loading.value = false }
}

function openCreate() {
  editing.value = null
  form.value = { name: '', code: '', description: '', isActive: true, resources: defaultResources() }
  visible.value = true
}

function openEdit(specification: HostedRunnerSpecification) {
  editing.value = specification
  form.value = { name: specification.name, description: specification.description ?? '', isActive: specification.isActive, resources: { ...specification.resources } }
  visible.value = true
}

async function save() {
  if (!form.value.name.trim() || (!editing.value && !form.value.code?.trim())) return
  saving.value = true
  try {
    if (editing.value) await adminHostedRunnersService.updateSpecification(editing.value.id, form.value)
    else await adminHostedRunnersService.createSpecification(form.value)
    visible.value = false
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Hosted runner specification saved', life: 2000 })
    await load()
  } catch { toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to save specification', life: 3000 }) }
  finally { saving.value = false }
}

async function toggle(specification: HostedRunnerSpecification) {
  try { await adminHostedRunnersService.setSpecificationActive(specification.id, !specification.isActive); await load() }
  catch { toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to update specification status', life: 3000 }) }
}

async function duplicate(specification: HostedRunnerSpecification) {
  try { await adminHostedRunnersService.duplicateSpecification(specification.id); await load() }
  catch { toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to duplicate specification', life: 3000 }) }
}

async function saveSettings() {
  if (!settings.value) return
  try { settings.value = await adminHostedRunnersService.updateSpecificationSettings(settings.value); toast.add({ severity: 'success', summary: 'Saved', detail: 'Hosted runner settings updated', life: 2000 }) }
  catch { toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to update hosted runner settings', life: 3000 }) }
}

onMounted(load)
</script>

<template>
  <div class="flex flex-column gap-4">
    <div class="flex align-items-center justify-content-between">
      <div><h1 class="m-0">Hosted Runners</h1><div class="text-color-secondary text-sm mt-2">Manage the runtime contract used by each hosted runner.</div></div>
      <Button label="New specification" icon="pi pi-plus" size="small" @click="openCreate" />
    </div>
    <HostedRunnersTabs />
    <div class="text-xl font-semibold">Specifications</div>
    <DataTable :value="specifications" :loading="loading" data-key="id" responsive-layout="scroll">
      <Column field="name" header="Name" style="min-width: 15rem" />
      <Column field="code" header="Code" />
      <Column field="revision" header="Revision" />
      <Column header="Resources" style="min-width: 14rem"><template #body="{ data }"><div class="flex flex-wrap gap-1"><Tag :value="`${data.resources.cpuLimit} CPU`" severity="secondary" /><Tag :value="`${data.resources.memoryLimitMb} MB`" severity="secondary" /><Tag :value="`${data.resources.maximumApps} apps`" severity="secondary" /></div></template></Column>
      <Column header="Usage"><template #body="{ data }">{{ data.runnerCount }} runners<span v-if="data.outdatedRunnerCount"> · {{ data.outdatedRunnerCount }} outdated</span></template></Column>
      <Column header="Status"><template #body="{ data }"><Tag v-if="data.isDefault" value="Default" severity="info" /><Tag v-else :value="data.isActive ? 'Active' : 'Inactive'" :severity="data.isActive ? 'success' : 'secondary'" /></template></Column>
      <Column header=""><template #body="{ data }"><div class="flex gap-1"><Button icon="pi pi-pencil" text size="small" @click="openEdit(data)" /><Button icon="pi pi-copy" text size="small" @click="duplicate(data)" /><Button v-if="!data.isDefault" :icon="data.isActive ? 'pi pi-eye-slash' : 'pi pi-eye'" text size="small" @click="toggle(data)" /></div></template></Column>
    </DataTable>
    <div v-if="settings" class="surface-card border-round p-3 flex flex-column gap-3">
      <div class="text-lg font-semibold">Platform settings</div>
      <div class="flex align-items-center gap-2"><Checkbox v-model="settings.provisioningEnabled" :binary="true" input-id="provisioningEnabled" /><label for="provisioningEnabled">Allow hosted runner provisioning</label></div>
      <div class="flex align-items-center gap-2"><Select v-model="settings.defaultSpecificationId" :options="specifications.filter(s => s.isActive)" option-label="name" option-value="id" class="w-20rem" /><Button label="Save settings" size="small" @click="saveSettings" /></div>
    </div>
  </div>
  <Dialog v-model:visible="visible" :header="editing ? 'Edit specification' : 'New specification'" modal style="width: 34rem">
    <div class="flex flex-column gap-3">
      <div><label class="block text-sm mb-1">Name *</label><InputText v-model="form.name" class="w-full" /></div>
      <div v-if="!editing"><label class="block text-sm mb-1">Code *</label><InputText v-model="form.code" class="w-full" /></div>
      <div><label class="block text-sm mb-1">Description</label><Textarea v-model="form.description" rows="2" class="w-full" /></div>
      <div class="grid">
        <div class="col-6"><label class="block text-sm mb-1">CPU limit</label><InputNumber v-model="form.resources.cpuLimit" :min="0.1" :max="8" :min-fraction-digits="2" class="w-full" /></div>
        <div class="col-6"><label class="block text-sm mb-1">Memory MB</label><InputNumber v-model="form.resources.memoryLimitMb" :min="256" class="w-full" /></div>
        <div class="col-6"><label class="block text-sm mb-1">Reservation MB</label><InputNumber v-model="form.resources.memoryReservationMb" :min="64" class="w-full" /></div>
        <div class="col-6"><label class="block text-sm mb-1">Disk MB</label><InputNumber v-model="form.resources.diskLimitMb" :min="512" class="w-full" /></div>
        <div class="col-6"><label class="block text-sm mb-1">PID limit</label><InputNumber v-model="form.resources.pidsLimit" :min="32" class="w-full" /></div>
        <div class="col-6"><label class="block text-sm mb-1">Maximum apps</label><InputNumber v-model="form.resources.maximumApps" :min="1" class="w-full" /></div>
      </div>
      <div v-if="editing" class="flex align-items-center gap-2"><Checkbox v-model="form.isActive" :binary="true" input-id="isActive" /><label for="isActive">Active</label></div>
    </div>
    <template #footer><Button label="Cancel" text @click="visible = false" /><Button label="Save" icon="pi pi-check" :loading="saving" :disabled="!form.name.trim() || (!editing && !form.code?.trim())" @click="save" /></template>
  </Dialog>
</template>
