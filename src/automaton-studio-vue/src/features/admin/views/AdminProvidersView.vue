<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import { adminAiProvidersService } from '../services/adminAiProvidersService'
import type { AiProviderAdmin } from '../types/admin.types'

const router = useRouter()
const toast = useToast()
const confirm = useConfirm()

const providers = ref<AiProviderAdmin[]>([])
const loading = ref(false)

const sortedProviders = computed(() =>
  [...providers.value].sort((a, b) => {
    const aReady = isReady(a)
    const bReady = isReady(b)
    if (aReady !== bReady) return aReady ? 1 : -1
    return a.sortOrder - b.sortOrder
  })
)

function isReady(p: AiProviderAdmin): boolean {
  if (!p.requiresApiKey) return true
  return p.hasSystemKey && p.hasDefaultModel
}

function readinessLabel(p: AiProviderAdmin): string {
  if (!p.requiresApiKey) return 'Ready'
  if (!p.hasSystemKey && !p.hasDefaultModel) return 'No key · No default'
  if (!p.hasSystemKey) return 'No system key'
  if (!p.hasDefaultModel) return 'No default model'
  return 'Ready'
}

function readinessSeverity(p: AiProviderAdmin): 'success' | 'warning' | 'danger' {
  if (!p.requiresApiKey) return 'success'
  if (!p.hasSystemKey && !p.hasDefaultModel) return 'danger'
  if (!p.hasSystemKey || !p.hasDefaultModel) return 'warning'
  return 'success'
}

onMounted(loadProviders)

async function loadProviders() {
  loading.value = true
  try {
    providers.value = await adminAiProvidersService.getProviders()
  } catch {
    toast.add({ severity: 'error', summary: 'Load Failed', detail: 'Failed to load AI providers', life: 3000 })
  } finally {
    loading.value = false
  }
}

function confirmDelete(provider: AiProviderAdmin) {
  confirm.require({
    message: `Delete "${provider.displayName}"? This will also delete all its models.`,
    header: 'Delete Provider',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await adminAiProvidersService.deleteProvider(provider.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: 'Provider deleted', life: 3000 })
        await loadProviders()
      } catch (error: unknown) {
        const axiosError = error as { response?: { data?: { message?: string } } }
        toast.add({ severity: 'error', summary: 'Delete Failed', detail: axiosError?.response?.data?.message || 'Failed to delete provider', life: 3000 })
      }
    },
  })
}

function goToDetail(provider: AiProviderAdmin) {
  router.push({ name: 'admin-provider-detail', params: { providerId: provider.id } })
}
</script>

<template>
  <div class="admin-providers-view">
    <div class="page-header mb-4">
      <div class="flex align-items-center gap-2 mb-2">
        <i class="pi pi-shield text-color-secondary"></i>
        <span class="text-color-secondary text-sm">Administration</span>
      </div>
      <div class="flex align-items-center justify-content-between">
        <div>
          <h1 class="text-2xl font-bold m-0">AI Providers</h1>
          <p class="text-color-secondary mt-1 mb-0">Configure providers, models, and system API keys</p>
        </div>
        <Button label="Add Provider" icon="pi pi-plus" @click="router.push({ name: 'admin-provider-detail', params: { providerId: 'new' } })" />
      </div>
    </div>

    <Card>
      <template #content>
        <DataTable :value="sortedProviders" :loading="loading" striped-rows responsive-layout="scroll">
          <template #empty>
            <div class="text-center py-5 text-color-secondary">
              <i class="pi pi-box text-4xl mb-3 block"></i>
              <p class="m-0">No AI providers configured yet.</p>
            </div>
          </template>

          <Column field="displayName" header="Provider" style="min-width: 200px">
            <template #body="{ data }">
              <div class="font-medium">{{ data.displayName }}</div>
              <div class="text-sm text-color-secondary">{{ data.name }}</div>
            </template>
          </Column>

          <Column field="isActive" header="Visibility" style="width: 110px">
            <template #body="{ data }">
              <Tag :value="data.isActive ? 'Visible' : 'Hidden'" :severity="data.isActive ? 'success' : 'secondary'" />
            </template>
          </Column>

          <Column header="Readiness" style="width: 200px">
            <template #body="{ data }">
              <div class="flex align-items-center gap-2">
                <Tag :value="readinessLabel(data)" :severity="readinessSeverity(data)" />
                <i
                  v-if="!isReady(data) && data.requiresApiKey"
                  class="pi pi-info-circle text-color-secondary text-sm cursor-pointer"
                  title="Click the gear icon to add a system API key and set a default model."
                />
              </div>
            </template>
          </Column>

          <Column field="modelCount" header="Models" style="width: 110px">
            <template #body="{ data }">
              <Button
                :label="`${data.activeModelCount} / ${data.modelCount}`"
                text
                size="small"
                class="p-0"
                title="Manage models"
                @click="goToDetail(data)"
              />
            </template>
          </Column>

          <Column header="Actions" style="width: 130px" align="center">
            <template #body="{ data }">
              <div class="flex gap-1 justify-content-center">
                <Button
                  icon="pi pi-pencil"
                  text
                  rounded
                  severity="warning"
                  size="small"
                  title="Edit provider settings"
                  @click="goToDetail(data)"
                />
                <Button
                  icon="pi pi-trash"
                  text
                  rounded
                  severity="danger"
                  size="small"
                  title="Delete provider"
                  @click="confirmDelete(data)"
                />
              </div>
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>


  </div>
</template>

<style scoped>
.admin-providers-view {
  max-width: 1200px;
}
</style>