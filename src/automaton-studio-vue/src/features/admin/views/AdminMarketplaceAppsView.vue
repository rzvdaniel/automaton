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
import { getPublishedAppUrl } from '@/features/apps/utils/public-app-url'
import { adminSettingsService } from '../services/adminSettingsService'
import type { StoreMode } from '../types/admin.types'

interface MarketplaceApp {
  id: string
  name: string
  summary?: string | null
  publicUrl?: string | null
  thumbnailUrl?: string | null
  viewCount: number
  starCount: number
  isFeatured: boolean
  category?: string | null
  tags?: string | null
  reviewStatus: string
  accessStatus: string
  marketplaceModerationReason?: string | null
  ownerDisplayName?: string | null
  ownerPublicUsername?: string | null
}

interface SelectOption {
  label: string
  value: string | null
}

const toast = useToast()

const isLoading = ref(false)
const isSavingCategories = ref(false)
const search = ref('')
const selectedStatusFilter = ref<'all' | 'approved' | 'flagged' | 'active' | 'hidden' | 'disabled'>('all')
const selectedCategoryFilter = ref<string | null>('all')
const featuredOnly = ref(false)
const apps = ref<MarketplaceApp[]>([])
const categoriesText = ref('')
const savingAppIds = ref<string[]>([])

const reviewOptions = [
  { label: 'Approved', value: 'approved' },
  { label: 'Flagged', value: 'flagged' },
]

const accessOptions = [
  { label: 'Active', value: 'active' },
  { label: 'Hidden', value: 'hidden' },
  { label: 'Disabled', value: 'disabled' },
]

const storeModeOptions: { label: string; value: StoreMode }[] = [
  { label: 'Public', value: 'public' },
  { label: 'Disabled', value: 'disabled' },
]
const storeMode = ref<StoreMode>('public')
const isSavingStoreMode = ref(false)
// Installed servers serve the store beside Studio at store.<domain>.
const storeUrl = computed(() => window.location.origin.replace('//studio.', '//store.'))

onMounted(() => {
  void loadData()
  void loadStoreMode()
})

async function loadStoreMode() {
  try {
    storeMode.value = (await adminSettingsService.getStoreSettings()).mode
  } catch {
    toast.add({ severity: 'error', summary: 'App store', detail: 'Could not load the app store setting.', life: 4000 })
  }
}

async function saveStoreMode(mode: StoreMode) {
  isSavingStoreMode.value = true
  try {
    storeMode.value = (await adminSettingsService.saveStoreSettings({ mode })).mode
    toast.add({ severity: 'success', summary: 'App store', detail: mode === 'public' ? 'The app store is public.' : 'The app store is turned off.', life: 3000 })
  } catch {
    toast.add({ severity: 'error', summary: 'App store', detail: 'Could not save the app store setting.', life: 4000 })
    await loadStoreMode()
  } finally {
    isSavingStoreMode.value = false
  }
}

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
  { label: 'Uncategorized', value: null },
  ...categoryList.value.map(category => ({ label: category, value: category })),
])

const categoryFilterOptions = computed(() => [
  { label: 'All categories', value: 'all' },
  { label: 'Uncategorized', value: '__uncategorized__' },
  ...categoryList.value.map(category => ({ label: category, value: category })),
])

const featuredCount = computed(() => apps.value.filter(app => app.isFeatured).length)
const moderatedCount = computed(() => apps.value.filter(app => app.reviewStatus !== 'approved' || app.accessStatus !== 'active').length)
const categorizedCount = computed(() => apps.value.filter(app => !!app.category?.trim()).length)

const filteredApps = computed(() => {
  const query = search.value.trim().toLowerCase()
  return apps.value.filter(app => {
    if (selectedStatusFilter.value !== 'all') {
      const matchesReview = app.reviewStatus === selectedStatusFilter.value
      const matchesAccess = app.accessStatus === selectedStatusFilter.value
      if (!matchesReview && !matchesAccess) {
        return false
      }
    }

    if (selectedCategoryFilter.value === '__uncategorized__' && app.category?.trim()) {
      return false
    }

    if (
      selectedCategoryFilter.value &&
      selectedCategoryFilter.value !== 'all' &&
      selectedCategoryFilter.value !== '__uncategorized__' &&
      app.category !== selectedCategoryFilter.value
    ) {
      return false
    }

    if (featuredOnly.value && !app.isFeatured) {
      return false
    }

    if (!query) {
      return true
    }

    return [
      app.name,
      app.summary ?? '',
      app.category ?? '',
      app.tags ?? '',
      app.ownerDisplayName ?? '',
      app.ownerPublicUsername ?? '',
      app.publicUrl ?? '',
    ].some(field => field.toLowerCase().includes(query))
  })
})

function isSavingApp(appId: string): boolean {
  return savingAppIds.value.includes(appId)
}

function appHref(app: MarketplaceApp): string | null {
  return app.publicUrl?.trim() ? getPublishedAppUrl(app.publicUrl) : null
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
    await apiClient.put(`/api/admin/marketplace-apps/${app.id}`, {
      isFeatured: app.isFeatured,
      category: app.category?.trim() || null,
      reviewStatus: app.reviewStatus,
      accessStatus: app.accessStatus,
      marketplaceModerationReason: app.marketplaceModerationReason?.trim() || null,
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

function statusSeverity(status: string): 'success' | 'warn' | 'danger' | 'secondary' {
  if (status === 'approved' || status === 'active') return 'success'
  if (status === 'flagged' || status === 'hidden') return 'warn'
  if (status === 'disabled') return 'danger'
  return 'secondary'
}

function combinedStatusLabel(app: MarketplaceApp): string {
  return `${app.reviewStatus} / ${app.accessStatus}`
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
          Moderate visibility, feature standout apps, and keep storefront categories organized.
        </p>
      </div>
      <div class="header-actions">
        <InputText
          v-model="search"
          placeholder="Search apps, owners, tags..."
          class="search-input"
        />
        <Select
          v-model="selectedStatusFilter"
          :options="[
            { label: 'All statuses', value: 'all' },
            { label: 'Approved', value: 'approved' },
            { label: 'Flagged', value: 'flagged' },
            { label: 'Active', value: 'active' },
            { label: 'Hidden', value: 'hidden' },
            { label: 'Disabled', value: 'disabled' },
          ]"
          option-label="label"
          option-value="value"
          class="filter-select"
        />
        <Select
          v-model="selectedCategoryFilter"
          :options="categoryFilterOptions"
          option-label="label"
          option-value="value"
          class="filter-select"
        />
        <div class="feature-toggle feature-toggle--header">
          <Checkbox v-model="featuredOnly" :binary="true" input-id="featured-only" />
          <label for="featured-only" class="text-sm text-color-secondary">Featured only</label>
        </div>
        <Button
          icon="pi pi-refresh"
          outlined
          size="small"
          :loading="isLoading"
          @click="loadData"
        />
      </div>
    </div>

    <Card class="store-settings">
      <template #content>
        <div class="store-settings-row">
          <div>
            <h3 class="store-settings-title">App store</h3>
            <p class="text-color-secondary">
              <template v-if="storeMode === 'public'">
                Anyone can browse listed apps at
                <a :href="storeUrl" target="_blank" rel="noopener">{{ storeUrl }}</a>.
              </template>
              <template v-else>The public app store is turned off. Listed apps stay listed and reappear when it is turned on.</template>
            </p>
          </div>
          <Select
            v-model="storeMode"
            :options="storeModeOptions"
            option-label="label"
            option-value="value"
            :disabled="isSavingStoreMode"
            aria-label="App store visibility"
            @update:model-value="saveStoreMode"
          />
        </div>
      </template>
    </Card>

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
            <span class="stat-label">Flagged, hidden, or disabled</span>
            <span class="stat-value">{{ moderatedCount }}</span>
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
              One category per line. These drive the storefront tabs developers can publish into.
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

        <div v-if="categoryList.length > 0" class="category-pill-row">
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

      <Column field="name" header="App" sortable style="min-width: 330px">
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
                <Tag :value="combinedStatusLabel(data)" :severity="statusSeverity(data.accessStatus === 'disabled' ? data.accessStatus : (data.reviewStatus === 'flagged' ? data.reviewStatus : data.accessStatus))" />
                <Tag v-if="data.isFeatured" value="Featured" severity="warn" />
              </div>
              <div class="text-sm text-color-secondary">
                By {{ data.ownerDisplayName || 'Unknown developer' }}
                <span v-if="data.ownerPublicUsername"> · {{ data.ownerPublicUsername }}</span>
              </div>
              <div class="app-summary text-color-secondary">
                {{ data.summary?.trim() || 'No summary yet.' }}
              </div>
              <div class="text-xs text-color-secondary">
                Developer tags:
                {{ data.tags?.trim() || 'None' }}
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

      <Column field="starCount" header="Stars" sortable style="width: 110px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <span class="num-cell">
            <i class="pi pi-star-fill mr-1 text-yellow-500"></i>{{ data.starCount.toLocaleString() }}
          </span>
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

      <Column header="Review" style="min-width: 170px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <Select
            v-model="data.reviewStatus"
            :options="reviewOptions"
            option-label="label"
            option-value="value"
            class="w-full"
          />
        </template>
      </Column>

      <Column header="Access" style="min-width: 170px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <Select
            v-model="data.accessStatus"
            :options="accessOptions"
            option-label="label"
            option-value="value"
            class="w-full"
          />
        </template>
      </Column>

      <Column header="Moderation Note" style="min-width: 260px">
        <template #body="{ data }: { data: MarketplaceApp }">
          <Textarea
            v-model="data.marketplaceModerationReason"
            rows="3"
            auto-resize
            class="w-full"
            placeholder="Optional internal moderation reason"
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
.store-settings {
  margin-bottom: 1rem;
}

.store-settings-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.store-settings-title {
  margin: 0 0 0.25rem;
}

.store-settings-row p {
  margin: 0;
}

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
  width: 240px;
}

.filter-select {
  min-width: 180px;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
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

.feature-toggle--header {
  min-height: 2.5rem;
}

@media (max-width: 1200px) {
  .summary-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 768px) {
  .summary-grid {
    grid-template-columns: 1fr;
  }

  .search-input {
    width: 100%;
  }

  .header-actions {
    width: 100%;
  }

  .filter-select {
    width: 100%;
  }

  .app-cell {
    flex-direction: column;
  }
}
</style>
