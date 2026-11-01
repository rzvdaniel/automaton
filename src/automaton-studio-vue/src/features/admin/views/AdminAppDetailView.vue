<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useAuthStore } from '@/features/auth/stores/auth.store'
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
import type { AdminAppPermissionStatus } from '../services/adminAppsService'

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
const permissions = ref<AdminAppPermissionStatus[]>([])
const savingPermission = ref<string | null>(null)

const appId = computed(() => String(route.params.appId || ''))
const ownerUserId = computed(() => String(route.query.ownerUserId || details.value?.ownerUserId || ''))
const ownerUserName = computed(() => String(route.query.ownerUserName || details.value?.ownerName || 'User'))
// Opened from Admin → Apps (or an approval link): breadcrumbs and Back return there.
// Decided from the link alone, so the breadcrumb does not change once the details load.
const fromAppsList = computed(() => route.query.from === 'apps' || !route.query.ownerUserId)
// Tab order: Overview, Permissions, Source Code. `?tab=permissions` opens the approval tab directly.
// Only system administrators approve permissions (the endpoint is SysAdmin-only), so only they get the tab.
const authStore = useAuthStore()
const isSystemAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const activeTab = ref(isSystemAdministrator.value && route.query.tab === 'permissions' ? 1 : 0)
const activeLanguage = computed(() => activePath.value ? getLanguageFromExtension(activePath.value) : 'plaintext')

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
    if (isSystemAdministrator.value) void loadPermissions()

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

async function loadPermissions() {
  try {
    permissions.value = await adminAppsService.getPermissions(appId.value)
  } catch {
    toast.add({ severity: 'error', summary: 'Failed to load app permissions', life: 3000 })
  }
}

async function setPermission(item: AdminAppPermissionStatus, approved: boolean) {
  savingPermission.value = item.permission
  const summary = approved ? 'Permission approved' : item.approved ? 'Permission revoked' : 'Permission rejected'
  try {
    permissions.value = await adminAppsService.setPermission(appId.value, item.permission, approved)
    toast.add({ severity: 'success', summary, life: 2500 })
  } catch {
    toast.add({ severity: 'error', summary: 'Permission update failed', life: 3000 })
  } finally {
    savingPermission.value = null
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
  if (fromAppsList.value) {
    void router.push({ name: 'admin-apps' })
    return
  }
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

async function saveModeration() {
  if (!details.value) {
    return
  }

  isSaving.value = true
  try {
    await adminAppsService.updateModeration(details.value.app.id, {
      reviewStatus: reviewStatus.value,
      accessStatus: accessStatus.value,
      marketplaceModerationReason: moderationReason.value.trim() || undefined,
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
          <template v-if="fromAppsList">
            <Button label="Apps" link class="breadcrumb-link" @click="router.push({ name: 'admin-apps' })" />
            <span class="breadcrumb-separator">/</span>
          </template>
          <template v-else>
            <Button label="Users" link class="breadcrumb-link" @click="router.push({ name: 'admin-users' })" />
            <span class="breadcrumb-separator">/</span>
            <Button label="User Apps" link class="breadcrumb-link" @click="goBack" />
            <span class="breadcrumb-separator">/</span>
          </template>
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
      <TabView v-model:active-index="activeTab" class="admin-app-tabs">
        <TabPanel header="Overview" value="overview">
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
                      Hidden is for non-punitive removal from marketplace and public runtime access. Disabled is for enforcement or safety blocks. Setting access back to Active republishes the public app URL.
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
                  </div>
                </div>
              </template>
            </Card>
          </div>
        </TabPanel>

        <TabPanel v-if="isSystemAdministrator" header="Permissions" value="permissions">
          <Card class="detail-card">
            <template #title>App permissions</template>
            <template #content>
              <p>Apps receive access only after a system administrator approves a requested permission.</p>
              <div v-if="permissions.length === 0" class="empty-state">This app has not requested any permissions.</div>
              <div v-for="item in permissions" :key="item.permission" class="detail-item">
                <div><strong>{{ item.permission }}</strong> <Tag :value="item.status" /></div>
                <Button v-if="item.approved" label="Revoke" severity="danger" outlined :loading="savingPermission === item.permission" :disabled="savingPermission !== null" @click="setPermission(item, false)" />
                <template v-else>
                  <Button label="Approve" :loading="savingPermission === item.permission" :disabled="savingPermission !== null" @click="setPermission(item, true)" />
                  <Button v-if="item.status === 'Pending'" label="Reject" severity="secondary" outlined :disabled="savingPermission !== null" @click="setPermission(item, false)" />
                </template>
              </div>
            </template>
          </Card>
        </TabPanel>

        <TabPanel header="Source Code" value="source-code">
          <Card class="detail-card detail-card--workspace">
            <template #content>
              <Splitter class="viewer-splitter">
                <SplitterPanel :size="28" :min-size="20">
                  <div class="viewer-panel">
                    <FileTree
                      title="Stored Files"
                      :nodes="fileTree"
                      :active-path="activePath"
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
