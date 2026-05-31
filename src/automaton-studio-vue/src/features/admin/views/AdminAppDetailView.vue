<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import FileTree from '@/features/apps/components/FileTree.vue'
import CodeEditor from '@/features/apps/components/CodeEditor.vue'
import { getLanguageFromExtension, type FileNode } from '@/features/apps/types/apps.types'
import type { AdminAppDetails } from '../types/admin.types'
import { adminAppsService } from '../services/adminAppsService'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const isLoading = ref(false)
const isSaving = ref(false)
const details = ref<AdminAppDetails | null>(null)
const fileTree = ref<FileNode[]>([])
const activePath = ref<string | null>(null)
const activeContent = ref('')
const reviewStatus = ref('approved')
const accessStatus = ref('active')
const moderationReason = ref('')

const appId = computed(() => String(route.params.appId || ''))
const ownerUserId = computed(() => String(route.query.ownerUserId || details.value?.ownerUserId || ''))
const ownerUserName = computed(() => String(route.query.ownerUserName || details.value?.ownerName || 'User'))
const activeLanguage = computed(() => activePath.value ? getLanguageFromExtension(activePath.value) : 'plaintext')
const isPublished = computed(() => !!details.value?.app.isPublished)

const reviewOptions = [
  { label: 'Approved', value: 'approved' },
  { label: 'Flagged', value: 'flagged' },
]

const accessOptions = [
  { label: 'Active', value: 'active' },
  { label: 'Hidden', value: 'hidden' },
  { label: 'Disabled', value: 'disabled' },
]

watch(details, (value) => {
  reviewStatus.value = value?.app.reviewStatus || 'approved'
  accessStatus.value = value?.app.accessStatus || 'active'
  moderationReason.value = value?.app.marketplaceModerationReason || ''
})

async function loadApp() {
  if (!appId.value) {
    return
  }

  isLoading.value = true
  try {
    const [appDetails, files] = await Promise.all([
      adminAppsService.getApp(appId.value),
      adminAppsService.getRootFileTree(appId.value),
    ])

    details.value = appDetails
    fileTree.value = files

    const firstReadableFile = findFirstFile(files)
    if (firstReadableFile) {
      await openFile(firstReadableFile)
    }
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load admin app details',
      life: 3000,
    })
  } finally {
    isLoading.value = false
  }
}

function findFirstFile(nodes: FileNode[]): string | null {
  for (const node of nodes) {
    if (node.type === 'file') {
      return node.path
    }

    if (node.children?.length) {
      const child = findFirstFile(node.children)
      if (child) {
        return child
      }
    }
  }

  return null
}

async function openFile(path: string) {
  activePath.value = path
  activeContent.value = ''

  if (!canPreviewPath(path)) {
    activeContent.value = 'Binary or non-text file preview is not available in the admin viewer.'
    return
  }

  try {
    activeContent.value = await adminAppsService.readRootFile(appId.value, path)
  } catch {
    activeContent.value = 'Failed to load file.'
  }
}

function canPreviewPath(path: string): boolean {
  const extension = path.split('.').pop()?.toLowerCase() || ''
  return [
    'html', 'htm', 'css', 'js', 'ts', 'json', 'md', 'markdown', 'xml', 'svg', 'py', 'txt', 'bat', 'vue', 'scss', 'sass', 'less', 'yml', 'yaml'
  ].includes(extension)
}

function goBack() {
  if (ownerUserId.value) {
    void router.push({
      name: 'admin-user-apps',
      params: { userId: ownerUserId.value },
      query: { userName: ownerUserName.value },
    })
    return
  }

  void router.push({ name: 'admin-users' })
}

async function unpublishApp() {
  if (!details.value) {
    return
  }

  isSaving.value = true
  try {
    await adminAppsService.unpublish(details.value.app.id)
    details.value.app.isPublished = false
    details.value.app.publicUrl = undefined
    details.value.app.publishedAt = undefined
    toast.add({
      severity: 'success',
      summary: 'App unpublished',
      detail: `${details.value.app.name} is no longer public.`,
      life: 2500,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Unpublish failed',
      detail: 'Could not unpublish this app.',
      life: 3000,
    })
  } finally {
    isSaving.value = false
  }
}

async function saveModeration() {
  if (!details.value) {
    return
  }

  isSaving.value = true
  try {
    await adminAppsService.updateModeration(details.value.app.id, {
      reviewStatus: reviewStatus.value,
      accessStatus: accessStatus.value,
      marketplaceModerationReason: moderationReason.value.trim() || null,
    })
    details.value.app.reviewStatus = reviewStatus.value
    details.value.app.accessStatus = accessStatus.value
    details.value.app.marketplaceModerationReason = moderationReason.value.trim() || null
    toast.add({
      severity: 'success',
      summary: 'Moderation updated',
      detail: 'Admin moderation settings were saved.',
      life: 2500,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Save failed',
      detail: 'Could not save moderation settings.',
      life: 3000,
    })
  } finally {
    isSaving.value = false
  }
}

function formatDate(value?: string) {
  if (!value) {
    return '—'
  }

  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString()
}

function formatSize(bytes?: number) {
  const value = bytes ?? 0
  if (value < 1024) {
    return `${value} B`
  }

  const kb = value / 1024
  if (kb < 1024) {
    return `${kb.toFixed(1)} KB`
  }

  const mb = kb / 1024
  if (mb < 1024) {
    return `${mb.toFixed(1)} MB`
  }

  const gb = mb / 1024
  return `${gb.toFixed(1)} GB`
}

function reviewSeverity(status?: string) {
  switch (status) {
    case 'approved':
      return 'success'
    case 'flagged':
      return 'warn'
    default:
      return 'secondary'
  }
}

function accessSeverity(status?: string) {
  switch (status) {
    case 'active':
      return 'success'
    case 'hidden':
      return 'warn'
    case 'disabled':
      return 'danger'
    default:
      return 'secondary'
  }
}

onMounted(() => {
  void loadApp()
})
</script>

<template>
  <div class="admin-app-detail-view">
    <div class="page-header">
      <div>
        <div class="breadcrumb-row">
          <Button label="Users" link class="breadcrumb-link" @click="router.push({ name: 'admin-users' })" />
          <span class="breadcrumb-separator">/</span>
          <Button v-if="ownerUserId" label="User Apps" link class="breadcrumb-link" @click="goBack" />
          <span v-if="ownerUserId" class="breadcrumb-separator">/</span>
          <span class="breadcrumb-current">{{ details?.app.name || 'App details' }}</span>
        </div>
        <h2 class="page-title">{{ details?.app.name || 'Admin App Details' }}</h2>
        <p class="page-subtitle">Read-only inspection, deployment context, and moderation for this app.</p>
      </div>

      <Button icon="pi pi-arrow-left" label="Back" outlined @click="goBack" />
    </div>

    <div v-if="isLoading && !details" class="loading-state">
      <ProgressSpinner style="width: 40px; height: 40px" />
    </div>

    <template v-else-if="details">
      <TabView class="admin-app-tabs">
        <TabPanel header="Overview">
          <div class="summary-grid">
            <Card class="detail-card">
              <template #title>Overview</template>
              <template #content>
                <dl class="detail-list detail-list--compact">
                  <div class="detail-item"><dt>Name</dt><dd>{{ details.app.name }}</dd></div>
                  <div class="detail-item"><dt>Owner</dt><dd>{{ details.ownerName }}</dd></div>
                  <div class="detail-item"><dt>Email</dt><dd>{{ details.ownerEmail }}</dd></div>
                  <div class="detail-item"><dt>Username</dt><dd>{{ details.ownerPublicUsername || '—' }}</dd></div>
                  <div class="detail-item"><dt>Project</dt><dd>{{ details.projectName || 'Personal' }}</dd></div>
                  <div class="detail-item"><dt>Created</dt><dd>{{ formatDate(details.app.created) }}</dd></div>
                  <div class="detail-item"><dt>Updated</dt><dd>{{ formatDate(details.app.updated) }}</dd></div>
                </dl>
              </template>
            </Card>

            <Card class="detail-card">
              <template #title>Deployment</template>
              <template #content>
                <dl class="detail-list detail-list--compact">
                  <div class="detail-item">
                    <dt>Status</dt>
                    <dd><Tag :value="details.app.isPublished ? 'Published' : 'Draft'" :severity="details.app.isPublished ? 'success' : 'secondary'" /></dd>
                  </div>
                  <div class="detail-item"><dt>Public URL</dt><dd>{{ details.app.publicUrl || '—' }}</dd></div>
                  <div class="detail-item"><dt>Published</dt><dd>{{ formatDate(details.app.publishedAt) }}</dd></div>
                  <div class="detail-item"><dt>Runner</dt><dd>{{ details.executionRunnerName || 'Unassigned' }}</dd></div>
                  <div class="detail-item"><dt>Version</dt><dd>{{ details.app.version }}</dd></div>
                  <div class="detail-item"><dt>Auth</dt><dd>{{ details.app.requiresAuthentication ? 'Protected' : 'Public' }}</dd></div>
                </dl>
              </template>
            </Card>

            <Card class="detail-card">
              <template #title>Statistics</template>
              <template #content>
                <dl class="detail-list detail-list--compact">
                  <div class="detail-item"><dt>Source Files</dt><dd>{{ details.sourceFileCount }}</dd></div>
                  <div class="detail-item"><dt>Published Files</dt><dd>{{ details.publishedFileCount }}</dd></div>
                  <div class="detail-item"><dt>Backend Files</dt><dd>{{ details.backendFileCount }}</dd></div>
                  <div class="detail-item"><dt>Size</dt><dd>{{ formatSize(details.totalSizeBytes) }}</dd></div>
                </dl>
              </template>
            </Card>

            <Card class="detail-card">
              <template #title>Moderation</template>
              <template #content>
                <div class="moderation-stack">
                  <div class="moderation-tags">
                    <Tag :value="`Review: ${details.app.reviewStatus}`" :severity="reviewSeverity(details.app.reviewStatus)" />
                    <Tag :value="`Access: ${details.app.accessStatus}`" :severity="accessSeverity(details.app.accessStatus)" />
                  </div>
                  <div class="moderation-field">
                    <label class="moderation-label">Review Status</label>
                    <Select
                      v-model="reviewStatus"
                      :options="reviewOptions"
                      option-label="label"
                      option-value="value"
                      class="w-full"
                    />
                  </div>
                  <div class="moderation-field">
                    <label class="moderation-label">Access Status</label>
                    <Select
                      v-model="accessStatus"
                      :options="accessOptions"
                      option-label="label"
                      option-value="value"
                      class="w-full"
                    />
                    <small class="moderation-help">
                      Hidden removes the app from marketplace and public runtime access. Disabled blocks it for enforcement or safety reasons.
                    </small>
                  </div>
                  <div class="moderation-field">
                    <label class="moderation-label">Moderation Note</label>
                    <Textarea
                      v-model="moderationReason"
                      rows="3"
                      auto-resize
                      class="w-full"
                      placeholder="Optional moderation note"
                    />
                  </div>
                  <div class="action-row">
                    <Button
                      label="Save Moderation"
                      icon="pi pi-save"
                      outlined
                      :loading="isSaving"
                      @click="saveModeration"
                    />
                    <Button
                      v-if="isPublished"
                      label="Unpublish"
                      icon="pi pi-eye-slash"
                      severity="warn"
                      outlined
                      :loading="isSaving"
                      @click="unpublishApp"
                    />
                  </div>
                </div>
              </template>
            </Card>
          </div>
        </TabPanel>

        <TabPanel header="Source Code">
          <Card class="detail-card detail-card--workspace">
            <template #content>
              <Splitter class="viewer-splitter">
                <SplitterPanel :size="28" :min-size="20">
                  <div class="viewer-panel">
                    <FileTree
                      title="Stored Files"
                      :nodes="fileTree"
                      :active-path="activePath"
                      :show-create="false"
                      :allow-delete="false"
                      empty-message="No files available for this app."
                      @open-file="openFile"
                    />
                  </div>
                </SplitterPanel>
                <SplitterPanel :size="72" :min-size="40">
                  <div class="viewer-panel">
                    <div class="code-header">
                      <span class="code-path">{{ activePath || 'Select a file' }}</span>
                    </div>
                    <div v-if="activePath" class="code-editor-shell">
                      <CodeEditor
                        :file-path="activePath"
                        :content="activeContent"
                        :language="activeLanguage"
                        :read-only="true"
                      />
                    </div>
                    <div v-else class="empty-code-state">Select a file to inspect its contents.</div>
                  </div>
                </SplitterPanel>
              </Splitter>
            </template>
          </Card>
        </TabPanel>
      </TabView>
    </template>
  </div>
</template>

<style scoped>
.admin-app-detail-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  flex-wrap: wrap;
}

.breadcrumb-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  margin-bottom: 0.5rem;
}

.breadcrumb-link {
  padding: 0;
}

.breadcrumb-separator,
.breadcrumb-current,
.page-subtitle,
.detail-list dt,
.stat-label {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.page-title {
  margin: 0;
  font-size: 1.75rem;
  font-weight: 700;
}

.page-subtitle {
  margin: 0.35rem 0 0;
}

.loading-state {
  display: flex;
  justify-content: center;
  padding: 2rem 0;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
}

.admin-app-tabs {
  display: flex;
  flex-direction: column;
}

.detail-card {
  height: 100%;
}

.detail-card--workspace :deep(.p-card-body),
.detail-card--workspace :deep(.p-card-content) {
  height: 100%;
}

.detail-list {
  display: grid;
  gap: 0.75rem;
  margin: 0;
}

.detail-item {
  display: grid;
  grid-template-columns: minmax(110px, 140px) minmax(0, 1fr);
  align-items: start;
  column-gap: 1rem;
}

.detail-item dt {
  font-size: 0.875rem;
}

.detail-item dd {
  margin: 0;
  font-weight: 500;
  word-break: break-word;
}

.moderation-stack {
  display: grid;
  gap: 0.85rem;
}

.moderation-tags {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.moderation-field {
  display: grid;
  gap: 0.35rem;
}

.moderation-label {
  font-size: 0.875rem;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.moderation-help {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
  line-height: 1.4;
}

.action-row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}


.viewer-splitter {
  min-height: 32rem;
  border: 1px solid var(--p-content-border-color);
  border-radius: 0.9rem;
  overflow: hidden;
}

.viewer-panel {
  height: 100%;
  min-height: 0;
  background: var(--p-surface-0);
}

.code-header {
  display: flex;
  align-items: center;
  min-height: 3rem;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
}

.code-path {
  font-family: Consolas, "Courier New", monospace;
  font-size: 0.9rem;
}

.code-editor-shell {
  height: calc(100% - 3rem);
  min-height: 28rem;
}

.empty-code-state {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 28rem;
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

@media (max-width: 960px) {
  .summary-grid {
    grid-template-columns: 1fr;
  }

  .viewer-splitter {
    min-height: 40rem;
  }
}
</style>
