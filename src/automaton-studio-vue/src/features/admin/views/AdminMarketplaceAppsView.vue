<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Checkbox from 'primevue/checkbox'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import InputText from 'primevue/inputtext'
import ProgressSpinner from 'primevue/progressspinner'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import apiClient from '@/shared/services/api/axios-instance'

interface MarketplaceApp {
  id: string
  name: string
  summary?: string | null
  publicUrl?: string | null
  thumbnailUrl?: string | null
  viewCount: number
  isFeatured: boolean
  category?: string | null
  tags?: string | null
}

interface SelectOption {
  label: string
  value: string | null
}

const toast = useToast()

const isLoading = ref(false)
const isSavingCategories = ref(false)
const search = ref('')
const apps = ref<MarketplaceApp[]>([])
const categoriesText = ref('')
const savingAppIds = ref<string[]>([])

const uncategorizedOption: SelectOption = {
  label: 'Uncategorized',
  value: null,
}

onMounted(loadData)

async function loadData() {
  isLoading.value = true
  try {
    const [appsResponse, categoriesResponse] = await Promise.all([
      apiClient.get<MarketplaceApp[]>('/api/admin/marketplace-apps'),
      apiClient.get<string[]>('/api/admin/marketplace-apps/categories'),
    ])

    apps.value = appsResponse.data ?? []
    categoriesText.value = (categoriesResponse.data ?? []).join('\n')
  } catch (error) {
    console.error('Failed to load marketplace apps', error)
    toast.add({
      severity: 'error',
      summary: 'Load failed',
      detail: 'Could not load marketplace apps or categories.',
      life: 3000,
    })
  } finally {
    isLoading.value = false
  }
}

function parseCategories(value: string): string[] {
  return Array.from(
    new Set(
      value
        .split(/[\n,]/)
        .map(item => item.trim())
        .filter(Boolean),
    ),
  )
}

const categoryList = computed(() => parseCategories(categoriesText.value))

const categoryOptions = computed<SelectOption[]>(() => [
  uncategorizedOption,
  ...categoryList.value.map(category => ({
    label: category,
    value: category,
  })),
])

const featuredCount = computed(() => apps.value.filter(app => app.isFeatured).length)

const categorizedCount = computed(() => apps.value.filter(app => !!app.category?.trim()).length)

const filteredApps = computed(() => {
  const query = search.value.trim().toLowerCase()

  if (!query) {
    return apps.value
  }

  return apps.value.filter(app => {
    const fields = [
      app.name,
      app.summary ?? '',
      app.category ?? '',
      app.tags ?? '',
      app.publicUrl ?? '',
    ]

    return fields.some(field => field.toLowerCase().includes(query))
  })
})

function isSavingApp(appId: string): boolean {
  return savingAppIds.value.includes(appId)
}

function appHref(app: MarketplaceApp): string | null {
  if (!app.publicUrl?.trim()) {
    return null
  }

  return `/web/${app.publicUrl}`
}

function appInitials(name: string): string {
  return name
    .split(/\s+/)
    .slice(0, 2)
    .map(part => part.charAt(0).toUpperCase())
    .join('')
}

async function saveCategories() {
  isSavingCategories.value = true

  try {
    const cleanedCategories = parseCategories(categoriesText.value)
    await apiClient.put('/api/admin/marketplace-apps/categories', cleanedCategories)
    categoriesText.value = cleanedCategories.join('\n')
    toast.add({
      severity: 'success',
      summary: 'Categories saved',
      detail: 'Marketplace categories were updated.',
      life: 2500,
    })
  } catch (error) {
    console.error('Failed to save marketplace categories', error)
    toast.add({
      severity: 'error',
      summary: 'Save failed',
      detail: 'Could not save marketplace categories.',
      life: 3000,
    })
  } finally {
    isSavingCategories.value = false
  }
}

async function saveApp(app: MarketplaceApp) {
  savingAppIds.value = [...savingAppIds.value, app.id]

  try {
    await apiClient.post(`/api/admin/marketplace-apps/${app.id}/promote`, {
      isFeatured: app.isFeatured,
      category: app.category?.trim() || null,
      tags: app.tags?.trim() || null,
    })

    toast.add({
      severity: 'success',
      summary: 'App updated',
      detail: `"${app.name}" marketplace settings were saved.`,
      life: 2500,
    })
  } catch (error) {
    console.error('Failed to save marketplace app', error)
    toast.add({
      severity: 'error',
      summary: 'Save failed',
      detail: `Could not save marketplace settings for "${app.name}".`,
      life: 3000,
    })
  } finally {
    savingAppIds.value = savingAppIds.value.filter(id => id !== app.id)
  }
}
</script>

<template>
  <div class="marketplace-admin-view">
    <div class="page-header">
      <div>
        <h2 class="page-title">
          <i class="pi pi-shop mr-2"></i>
          Marketplace Apps
        </h2>
        <p class="page-subtitle text-color-secondary">
          Curate the apps shown on automaton.monster, feature the strongest ones, and keep category navigation tidy.
        </p>
      </div>
      <div class="header-actions">
        <InputText
          v-model="search"
          placeholder="Search apps..."
          class="search-input"
        />
        <Button
          icon="pi pi-refresh"
          outlined
          size="small"
          :loading="isLoading"
          @click="loadData"
        />
      </div>
    </div>

    <div class="summary-grid">
      <Card>
        <template #content>
          <div class="stat-card">
            <span class="stat-label">Published apps</span>
            <span class="stat-value">{{ apps.length }}</span>
          </div>
        </template>
      </Card>

      <Card>
        <template #content>
          <div class="stat-card">
            <span class="stat-label">Featured apps</span>
            <span class="stat-value">{{ featuredCount }}</span>
          </div>
        </template>
      </Card>

      <Card>
        <template #content>
          <div class="stat-card">
            <span class="stat-label">Assigned to categories</span>
            <span class="stat-value">{{ categorizedCount }}</span>
          </div>
        </template>
      </Card>
    </div>

    <Card>
      <template #content>
        <div class="category-card-header">
          <div>
            <div class="section-title">Marketplace Categories</div>
            <div class="text-color-secondary text-sm">
              One category per line. These become the tabs users can assign their apps to.
            </div>
          </div>
          <Button
            label="Save Categories"
            icon="pi pi-save"
            size="small"
            :loading="isSavingCategories"
            @click="saveCategories"
          />
        </div>

        <Textarea
          v-model="categoriesText"
          rows="5"
          auto-resize
          class="w-full mt-3"
          placeholder="Productivity&#10;AI Tools&#10;Education"
        />

        <div class="category-pill-row" v-if="categoryList.length > 0">
          <Tag
            v-for="category in categoryList"
            :key="category"
            :value="category"
            severity="secondary"
          />
        </div>
      </template>
    </Card>

    <div v-if="isLoading" class="loading-state">
      <ProgressSpinner style="width: 40px; height: 40px" />
    </div>

    <DataTable
      v-else
      :value="filteredApps"
      :sort-field="'viewCount'"
      :sort-order="-1"
      striped-rows
      removable-sort
      class="apps-table"
    >
      <template #empty>
        <div class="empty-state">
          <i class="pi pi-box" style="font-size: 2rem; color: var(--p-text-muted-color)"></i>
          <p class="mt-2 text-color-secondary">No published apps matched your filters.</p>
        </div>
      </template>

      <Column field="name" header="App" sortable style="min-width: 300px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <div class="app-cell">
            <div v-if="data.thumbnailUrl" class="thumb-frame">
              <img :src="data.thumbnailUrl" :alt="`${data.name} thumbnail`" class="thumb-image" />
            </div>
            <div v-else class="thumb-fallback">
              {{ appInitials(data.name) }}
            </div>

            <div class="app-meta">
              <div class="app-meta-topline">
                <span class="app-name">{{ data.name }}</span>
                <Tag v-if="data.isFeatured" value="Featured" severity="warn" />
              </div>
              <div class="app-summary text-color-secondary">
                {{ data.summary?.trim() || 'No summary yet.' }}
              </div>
              <a
                v-if="appHref(data)"
                :href="appHref(data) || undefined"
                target="_blank"
                rel="noopener noreferrer"
                class="app-link"
              >
                {{ appHref(data) }}
              </a>
            </div>
          </div>
        </template>
      </Column>

      <Column field="viewCount" header="Views" sortable style="width: 110px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <span class="num-cell">{{ data.viewCount.toLocaleString() }}</span>
        </template>
      </Column>

      <Column header="Featured" style="width: 120px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <div class="feature-toggle">
            <Checkbox v-model="data.isFeatured" :binary="true" :input-id="`featured-${data.id}`" />
            <label :for="`featured-${data.id}`" class="text-sm text-color-secondary">Promote</label>
          </div>
        </template>
      </Column>

      <Column header="Category" style="min-width: 210px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <Select
            v-model="data.category"
            :options="categoryOptions"
            option-label="label"
            option-value="value"
            placeholder="Select category"
            class="w-full"
          />
        </template>
      </Column>

      <Column header="Tags" style="min-width: 220px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <InputText
            v-model="data.tags"
            class="w-full"
            placeholder="automation, analytics"
          />
        </template>
      </Column>

      <Column header="" style="width: 120px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <Button
            label="Save"
            icon="pi pi-save"
            size="small"
            :loading="isSavingApp(data.id)"
            @click="saveApp(data)"
          />
        </template>
      </Column>
    </DataTable>
  </div>
</template>

<style scoped>
.marketplace-admin-view {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.page-title {
  font-size: 1.25rem;
  font-weight: 700;
  margin: 0 0 0.25rem;
  display: flex;
  align-items: center;
}

.page-subtitle {
  font-size: 0.875rem;
  margin: 0;
  max-width: 56rem;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.search-input {
  width: 220px;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 1rem;
}

.stat-card {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.stat-label {
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
}

.stat-value {
  font-size: 1.75rem;
  font-weight: 700;
  line-height: 1;
}

.category-card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  flex-wrap: wrap;
}

.section-title {
  font-size: 1rem;
  font-weight: 600;
}

.category-pill-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 1rem;
}

.loading-state {
  display: flex;
  justify-content: center;
  padding: 4rem;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 3rem;
  text-align: center;
}

.app-cell {
  display: flex;
  align-items: flex-start;
  gap: 0.9rem;
}

.thumb-frame,
.thumb-fallback {
  width: 72px;
  height: 72px;
  border-radius: 16px;
  flex-shrink: 0;
}

.thumb-frame {
  overflow: hidden;
  background: var(--p-surface-100);
  border: 1px solid var(--p-content-border-color);
}

.thumb-image {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.thumb-fallback {
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  color: var(--p-primary-color);
  background:
    radial-gradient(circle at top left, color-mix(in srgb, var(--p-primary-color) 24%, transparent), transparent 58%),
    linear-gradient(135deg, var(--p-surface-100), var(--p-surface-200));
  border: 1px solid var(--p-content-border-color);
}

.app-meta {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.app-meta-topline {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.app-name {
  font-weight: 600;
}

.app-summary {
  font-size: 0.875rem;
  line-height: 1.4;
}

.app-link {
  font-size: 0.8rem;
  color: var(--p-primary-color);
  text-decoration: none;
}

.app-link:hover {
  text-decoration: underline;
}

.num-cell {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}

.feature-toggle {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

@media (max-width: 1024px) {
  .summary-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 768px) {
  .search-input {
    width: 100%;
  }

  .header-actions {
    width: 100%;
  }

  .app-cell {
    flex-direction: column;
  }
}
</style>
