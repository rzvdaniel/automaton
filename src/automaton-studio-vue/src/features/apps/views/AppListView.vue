<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import Dropdown from 'primevue/dropdown'
import Skeleton from 'primevue/skeleton'
import Message from 'primevue/message'
import Dialog from 'primevue/dialog'
import SelectButton from 'primevue/selectbutton'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import TabMenu from 'primevue/tabmenu'
import AppCard from '../components/AppCard.vue'
import RunnerSelector from '@/shared/components/ui/RunnerSelector.vue'
import { useAppsStore } from '../stores/apps.store'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import { getAvailableProviders } from '@/shared/services/ai/ai-providers-service'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { ConversationMode } from '@/shared/types/ai.types'
import AiModeModelSelector from '@/shared/components/ui/AiModeModelSelector.vue'
import type { App } from '../types/apps.types'
import { appsService } from '../services/apps.service'

const router = useRouter()
const toast = useToast()
const appsStore = useAppsStore()
const authStore = useAuthStore()
const projectsStore = useProjectsStore()
const { defaultAiModel } = useUserSettings()
const apiBase = import.meta.env.VITE_API_BASE_URL || ''

// AI generation state
const aiDescription = ref('')
const aiMode = ref<string>(ConversationMode.Autonomous)
const aiModel = ref<string>('')
const aiAvailableModels = ref<string[]>([])
const aiModelDisplayNames = ref<Record<string, string>>({})
const isLoadingModels = ref(false)

onMounted(async () => {
  isLoadingModels.value = true
  try {
    const providers = await getAvailableProviders()
    const models: string[] = []
    const displayNames: Record<string, string> = {}
    for (const p of providers) {
      const providerDisplayNames = p.modelDisplayNames || {}
      for (const m of p.supportedModels) {
        if (!models.includes(m)) models.push(m)
        if (!displayNames[m]) {
          displayNames[m] = providerDisplayNames[m] || m
        }
      }
    }
    aiAvailableModels.value = models.sort((a, b) => a.localeCompare(b))
    aiModelDisplayNames.value = displayNames

    // Priority: 1. user's saved preference, 2. admin default model, 3. first alphabetically
    if (defaultAiModel.value && models.includes(defaultAiModel.value)) {
      aiModel.value = defaultAiModel.value
    } else {
      let foundDefault = false
      for (const p of providers) {
        if (p.defaultModel && models.includes(p.defaultModel)) {
          aiModel.value = p.defaultModel
          foundDefault = true
          break
        }
      }
      if (!foundDefault && models.length > 0) {
        aiModel.value = models[0]!
      }
    }
  } catch {
    // Models unavailable — user can still type the prompt
  } finally {
    isLoadingModels.value = false
  }
})

// View mode state
const viewMode = ref('grid')
const viewOptions = ref([
  { icon: 'pi pi-th-large', value: 'grid' },
  { icon: 'pi pi-list', value: 'list' },
])

// Tab state
const activeTabIndex = ref(0)
const tabItems = ref([
  { label: 'My Apps', icon: 'pi pi-th-large' },
  { label: 'Templates', icon: 'pi pi-star' },
  { label: 'How to Build', icon: 'pi pi-book' },
])

// Templates state
const templates = ref<App[]>([])
const isLoadingTemplates = ref(false)
const isForkingId = ref<string | null>(null)

const fetchTemplates = async () => {
  isLoadingTemplates.value = true
  try {
    templates.value = await appsService.getTemplates()
  } catch {
    // silence — templates just won't show
  } finally {
    isLoadingTemplates.value = false
  }
}

const handleUseTemplate = async (template: App) => {
  isForkingId.value = template.id
  try {
    const newApp = await appsStore.forkTemplate(template.id)
    toast.add({
      severity: 'success',
      summary: 'Template Forked',
      detail: `"${newApp.name}" has been added to your apps`,
      life: 4000,
    })
    activeTabIndex.value = 0
    router.push(`/apps/${newApp.id}`)
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to use template'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  } finally {
    isForkingId.value = null
  }
}

watch(activeTabIndex, (index) => {
  if (index === 1 && templates.value.length === 0) {
    fetchTemplates()
  }
})

// Local state
const searchQuery = ref('')
const selectedStatus = ref<'all' | 'published' | 'draft'>('all')

const statusOptions = ref([
  { label: 'All', value: 'all' },
  { label: 'Published', value: 'published' },
  { label: 'Draft', value: 'draft' },
])

// Quick-start how-to tab
const quickStartPath = ref<'editor' | 'cli' | 'mcp'>('editor')

// Create dialog state
const showCreateDialog = ref(false)
const newAppName = ref('')
const selectedRunnerId = ref<string | null>(null)
const isCreating = ref(false)

// Publish dialog state
const showPublishDialog = ref(false)
const publishingApp = ref<App | null>(null)
const publicUrl = ref('')
const requiresAuth = ref(true)
const isPublishing = ref(false)
const urlError = ref('')

// Computed
const showEmptySearch = computed(() => {
  return (
    appsStore.filteredApps.length === 0 &&
    appsStore.hasApps &&
    (searchQuery.value || selectedStatus.value !== 'all')
  )
})

// Watch for filter changes
watch(searchQuery, (value) => {
  appsStore.setSearchFilter(value)
})

watch(selectedStatus, (value) => {
  appsStore.setStatusFilter(value)
})

// React to active project changes (including initial load)
watch(
  () => projectsStore.activeProjectId,
  async (projectId) => {
    try {
      await appsStore.fetchApps(projectId ?? undefined)
    } catch {
      // Error handled in store
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  appsStore.clearFilters()
})

// Handlers
const handleGenerateApp = async () => {
  if (!aiDescription.value.trim()) return

  isCreating.value = true
  try {
    // Extract a name from the description immediately (no AI call) so we navigate fast.
    // The editor page receives the full prompt and performs the expensive AI generation there.
    const words = aiDescription.value.trim().split(/\s+/).slice(0, 4).join(' ')
    const appName = words.length > 30 ? words.substring(0, 30) + '...' : words

    const app = await appsStore.createApp({
      name: appName,
    })

    // Navigate to editor with prompt, mode, and model as query params
    // Vue Router encodes query params automatically — don't call encodeURIComponent
    const query: Record<string, string> = { prompt: aiDescription.value.trim() }
    if (aiMode.value)  query.mode  = aiMode.value
    if (aiModel.value) query.model = aiModel.value
    router.push({ path: `/apps/${app.id}`, query })

    // Clear the description
    aiDescription.value = ''
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to create app'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  } finally {
    isCreating.value = false
  }
}

const handleViewExamples = () => {
  // TODO: Navigate to examples page or open modal with templates
  toast.add({
    severity: 'info',
    summary: 'Coming Soon',
    detail: 'Example apps will be available soon',
    life: 3000,
  })
}

const handleCreateApp = async () => {
  if (!newAppName.value.trim()) {
    toast.add({
      severity: 'warn',
      summary: 'Validation Error',
      detail: 'App name is required',
      life: 3000,
    })
    return
  }

  isCreating.value = true
  try {
    const app = await appsStore.createApp({
      name: newAppName.value.trim(),
      executionRunnerId: selectedRunnerId.value || undefined,
    })
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: `App "${app.name}" created successfully`,
      life: 3000,
    })
    showCreateDialog.value = false
    newAppName.value = ''
    selectedRunnerId.value = null
    router.push(`/apps/${app.id}`)
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to create app'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  } finally {
    isCreating.value = false
  }
}

const openCreateDialog = () => {
  newAppName.value = ''
  selectedRunnerId.value = null
  showCreateDialog.value = true
}

const viewPublicApps = () => {
  if (authStore.publicUsername) {
    window.open(`/${authStore.publicUsername}`, '_blank')
  } else {
    toast.add({
      severity: 'warn',
      summary: 'Profile not set',
      detail: 'Set your public username in Settings to view your public apps.',
      life: 4000,
    })
    router.push('/settings')
  }
}

const openPublishDialog = (app: App) => {
  publishingApp.value = app
  publicUrl.value = app.publicUrl || generateSlug(app.name)
  requiresAuth.value = true
  urlError.value = ''
  showPublishDialog.value = true
}

const generateSlug = (name: string): string => {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

const validateUrl = () => {
  if (!publicUrl.value.trim()) {
    urlError.value = 'URL slug is required'
    return false
  }
  if (!/^[a-z0-9-]+$/.test(publicUrl.value)) {
    urlError.value = 'URL can only contain lowercase letters, numbers, and hyphens'
    return false
  }
  urlError.value = ''
  return true
}

const handlePublishApp = async () => {
  if (!publishingApp.value) return
  if (!validateUrl()) return

  isPublishing.value = true
  try {
    await appsStore.publishApp(publishingApp.value.id, {
      publicUrl: publicUrl.value,
      requiresAuthentication: requiresAuth.value,
    })
    toast.add({
      severity: 'success',
      summary: 'Published',
      detail: `App is now available at /web/${publicUrl.value}`,
      life: 5000,
    })
    showPublishDialog.value = false
    publishingApp.value = null
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to publish app'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  } finally {
    isPublishing.value = false
  }
}

const handleRetry = async () => {
  appsStore.clearError()
  try {
    await appsStore.fetchApps()
  } catch {
    // Error is handled in the store
  }
}
</script>

<template>
  <div class="apps-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Apps</h1>
        <p class="text-color-secondary mt-1 mb-0">Build and publish web applications</p>
      </div>
      <div class="flex gap-2">
        <Button
          label="View Public Apps"
          icon="pi pi-external-link"
          outlined
          @click="viewPublicApps"
        />
      </div>
    </div>

    <!-- AI Generation Input -->
    <Card class="mb-5">
      <template #content>
        <div class="flex align-items-start gap-3">
          <div
            class="flex align-items-center justify-content-center border-round flex-shrink-0"
            style="
              width: 48px;
              height: 48px;
              background-color: var(--p-primary-100);
              color: var(--p-primary-600);
            "
          >
            <i class="pi pi-sparkles text-xl"></i>
          </div>
          <div class="flex-1">
            <div class="font-semibold mb-2">What would you like to build?</div>
            <Textarea
              v-model="aiDescription"
              rows="3"
              placeholder="Describe your app in plain English. For example: 'A customer feedback form that saves responses to a database and sends email notifications...'"
              class="w-full"
              @keydown="(e: KeyboardEvent) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleGenerateApp() } }"
            />
            <div class="flex align-items-center justify-content-between mt-3">
              <AiModeModelSelector
                v-model:mode="aiMode"
                v-model:model="aiModel"
                :available-models="aiAvailableModels"
                :model-display-names="aiModelDisplayNames"
                :loading="isLoadingModels"
              />
              <div class="flex align-items-center gap-2">
                <Button
                  label="Start from scratch"
                  icon="pi pi-plus"
                  severity="secondary"
                  outlined
                  :disabled="isCreating"
                  @click="openCreateDialog"
                />
                <Button
                  label="Create with AI"
                  icon="pi pi-sparkles"
                  :disabled="!aiDescription || isCreating"
                  :loading="isCreating"
                  @click="handleGenerateApp"
                />
              </div>
            </div>
          </div>
        </div>
      </template>
    </Card>

    <!-- Tabs: My Apps / Templates -->
    <TabMenu v-model:activeIndex="activeTabIndex" :model="tabItems" class="mb-4" />

    <!-- ─── MY APPS TAB ────────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 0">

    <!-- Apps List Header -->
    <div class="flex align-items-center justify-content-between mb-4">
      <div class="flex align-items-center gap-3">
        <span class="font-semibold">Your Apps</span>
        <Tag :value="`${appsStore.filteredApps.length} apps`" severity="secondary" />
      </div>
      <div class="flex align-items-center gap-3">
        <IconField>
          <InputIcon class="pi pi-search" />
          <InputText v-model="searchQuery" placeholder="Search apps..." />
        </IconField>
        <Dropdown
          v-model="selectedStatus"
          :options="statusOptions"
          option-label="label"
          option-value="value"
          placeholder="Status"
          class="w-10rem"
        />
        <SelectButton v-model="viewMode" :options="viewOptions" option-value="value">
          <template #option="{ option }">
            <i :class="option.icon"></i>
          </template>
        </SelectButton>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="appsStore.isLoading">
      <div class="grid">
        <div v-for="i in 6" :key="i" class="col-12 md:col-6 lg:col-4">
          <Card class="h-full">
            <template #content>
              <div class="flex flex-column gap-3">
                <div class="flex align-items-center gap-2">
                  <Skeleton shape="circle" size="1.5rem" />
                  <Skeleton width="60%" height="1.25rem" />
                </div>
                <Skeleton width="100%" height="2.5rem" />
                <Skeleton width="5rem" height="1.5rem" border-radius="1rem" />
                <Skeleton width="8rem" height="1rem" />
                <div class="flex gap-2 mt-2">
                  <Skeleton width="50%" height="2rem" />
                  <Skeleton width="50%" height="2rem" />
                </div>
              </div>
            </template>
          </Card>
        </div>
      </div>
    </div>

    <!-- Error State -->
    <Message v-else-if="appsStore.error" severity="error" :closable="false">
      <div class="flex align-items-center justify-content-between w-full">
        <span>{{ appsStore.error }}</span>
        <Button label="Retry" severity="danger" size="small" outlined @click="handleRetry" />
      </div>
    </Message>

    <!-- Content -->
    <template v-else>
      <!-- Apps Grid -->
      <div v-if="appsStore.filteredApps.length > 0" class="grid">
        <div
          v-for="app in appsStore.filteredApps"
          :key="app.id"
          class="col-12 md:col-6 lg:col-4"
        >
          <AppCard :app="app" @publish="openPublishDialog" />
        </div>
      </div>

      <!-- Empty Search Results -->
      <Card v-else-if="showEmptySearch">
        <template #content>
          <div class="text-center py-5">
            <i class="pi pi-search text-5xl text-color-secondary mb-4"></i>
            <h3 class="text-xl font-semibold mb-2">No apps found</h3>
            <p class="text-color-secondary mb-4">Try adjusting your search or filters</p>
            <Button
              label="Clear Filters"
              icon="pi pi-filter-slash"
              outlined
              @click="
                () => {
                  searchQuery = ''
                  selectedStatus = 'all'
                }
              "
            />
          </div>
        </template>
      </Card>

      <!-- Empty State (No Apps) -->
      <Card v-else-if="!appsStore.hasApps">
        <template #content>
          <div class="text-center py-5">
            <i class="pi pi-globe text-6xl text-color-secondary mb-4"></i>
            <h3 class="text-xl font-semibold mb-2">No apps yet</h3>
            <p class="text-color-secondary mb-4">
              Describe your app above and let AI generate it, or create one manually
            </p>
            <Button label="Create App" icon="pi pi-plus" @click="openCreateDialog" />
          </div>
        </template>
      </Card>

    </template><!-- /v-else -->
    </template><!-- /v-if="activeTabIndex === 0" -->

    <!-- ─── HOW TO BUILD TAB ─────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 2">
      <Card class="quick-start-card">
        <template #content>
          <div class="quick-start-header mb-3">
            <i class="pi pi-book text-xl" style="color: var(--p-primary-500)" />
            <span class="font-semibold ml-2">How to build an app</span>
          </div>

          <!-- Path tabs -->
          <div class="qs-tabs mb-4">
            <button
              :class="['qs-tab', quickStartPath === 'editor' && 'qs-tab--active']"
              @click="quickStartPath = 'editor'"
            >
              <i class="pi pi-desktop" />
              <span>AI Editor</span>
            </button>
            <button
              :class="['qs-tab', quickStartPath === 'cli' && 'qs-tab--active']"
              @click="quickStartPath = 'cli'"
            >
              <i class="pi pi-terminal" />
              <span>CLI + Your IDE</span>
            </button>
            <button
              :class="['qs-tab', quickStartPath === 'mcp' && 'qs-tab--active']"
              @click="quickStartPath = 'mcp'"
            >
              <i class="pi pi-sparkles" />
              <span>MCP Server</span>
            </button>
          </div>

          <!-- Editor path -->
          <div v-if="quickStartPath === 'editor'" class="qs-content">
            <p class="text-color-secondary text-sm mt-0 mb-3">
              The fastest way to get started. Type a description above and the AI writes the app for you.
            </p>
            <ol class="qs-steps">
              <li>Type what you want to build in the text area above</li>
              <li>Click <strong>Generate</strong> — the app is created and you're taken to the editor</li>
              <li>The AI writes the files and asks you to <strong>Deploy Backend</strong></li>
              <li>Preview opens automatically. Click <strong>Publish</strong> to share.</li>
            </ol>
            <Button label="Generate with AI" icon="pi pi-sparkles" size="small" class="mt-2" @click="handleGenerateApp" />
          </div>

          <!-- CLI path -->
          <div v-else-if="quickStartPath === 'cli'" class="qs-content">
            <p class="text-color-secondary text-sm mt-0 mb-3">
              Build locally with any editor (Cursor, VS Code…), keep the project in Git,
              and deploy with one command. MCP is recommended when supported, but not required.
            </p>
            <div class="qs-code mb-2">npm install -g @automaton/cli</div>
            <div class="qs-code mb-3">automaton deploy</div>
            <p class="text-color-secondary text-sm mb-2">
              Automaton apps need to follow a specific project structure, backend controller pattern,
              and frontend integration contract. If you are using an external AI tool without MCP,
              point it to <a href="https://automaton.monster/llms.txt" target="_blank" rel="noopener" class="qs-link">llms.txt</a>
              before it starts generating files.
            </p>
            <div class="qs-links mb-3">
              <a href="https://www.npmjs.com/package/@automaton/cli" target="_blank" rel="noopener" class="p-button p-button-sm p-button-secondary p-button-outlined" style="text-decoration:none">
                <i class="pi pi-external-link mr-1" />CLI docs
              </a>
              <Button
                label="Open Studio documentation"
                icon="pi pi-book"
                size="small"
                severity="secondary"
                outlined
                @click="router.push('/docs')"
              />
            </div>
            <p class="text-color-secondary text-sm mb-2">
              Use <a href="https://automaton.monster/llms.txt" target="_blank" rel="noopener" class="qs-link">llms.txt</a>
              when you want the exact structure and AI instructions. Use
              <button type="button" class="qs-inline-link" @click="router.push('/docs')">Studio documentation</button>
              when you want a more guided walkthrough with quick starts and examples.
            </p>
          </div>

          <!-- MCP path -->
          <div v-else class="qs-content">
            <p class="text-color-secondary text-sm mt-0 mb-3">
              Connect Automaton as an MCP server to GitHub Copilot Agent or Claude Code.
              The AI can understand the required Automaton structure while you keep working in your
              local project. This is the most accurate path when your tool supports MCP.
            </p>
            <p class="text-sm font-semibold mb-1">VS Code / GitHub Copilot — add to <code class="qs-inline-code">.vscode/mcp.json</code>:</p>
            <div class="qs-code qs-code--block mb-3">
              <div>&#123;</div>
              <div class="pl-3">"servers": &#123; "automaton": &#123;</div>
              <div class="pl-5">"type": "http",</div>
              <div class="pl-5">"url": "{{ apiBase }}/api/mcp",</div>
              <div class="pl-5">"headers": &#123; "X-API-Key": "<em>your-api-key</em>" &#125;</div>
              <div class="pl-3">&#125; &#125;</div>
              <div>&#125;</div>
            </div>
            <p class="text-sm font-semibold mb-1">Claude Code:</p>
            <div class="qs-code mb-3">claude mcp add automaton --transport http "{{ apiBase }}/api/mcp" --header "X-API-Key: your-api-key"</div>
            <p class="text-color-secondary text-sm mb-3">
              Replace <code class="qs-inline-code">your-api-key</code> with a key from
              <strong
                class="cursor-pointer"
                style="color: var(--p-primary-500)"
                @click="router.push('/apikeys')"
              >Settings → API Keys</strong> (scope: <em>execute</em>).
              Then tell the AI that backend controllers live in <code class="qs-inline-code">backend/</code>,
              export <code class="qs-inline-code">handle(action_name, inputs)</code>, and frontend code should use
              <code class="qs-inline-code">window.automatonApi.callController(...)</code> with <code class="qs-inline-code">try/catch</code>.
              When the project is ready, run <code class="qs-inline-code">automaton deploy</code>.
            </p>
          </div>
        </template>
      </Card>
    </template><!-- /v-if="activeTabIndex === 2" -->

    <!-- ─── TEMPLATES TAB ────────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 1">
      <!-- Loading -->
      <div v-if="isLoadingTemplates" class="grid">
        <div v-for="i in 6" :key="i" class="col-12 md:col-6 lg:col-4">
          <Card class="h-full">
            <template #content>
              <div class="flex flex-column gap-3">
                <Skeleton width="70%" height="1.5rem" />
                <Skeleton width="100%" height="3rem" />
                <Skeleton width="5rem" height="1.5rem" border-radius="1rem" />
                <Skeleton width="100%" height="2.5rem" />
              </div>
            </template>
          </Card>
        </div>
      </div>

      <!-- Empty -->
      <Card v-else-if="templates.length === 0">
        <template #content>
          <div class="text-center py-5">
            <i class="pi pi-star text-6xl text-color-secondary mb-4"></i>
            <h3 class="text-xl font-semibold mb-2">No templates yet</h3>
            <p class="text-color-secondary mb-0">
              Published template apps will appear here so you can fork them into your account.
            </p>
          </div>
        </template>
      </Card>

      <!-- Template Cards -->
      <div v-else class="grid">
        <div
          v-for="template in templates"
          :key="template.id"
          class="col-12 md:col-6 lg:col-4"
        >
          <Card class="template-card h-full">
            <template #header>
              <div class="template-thumbnail">
                <img
                  v-if="template.thumbnailUrl"
                  :src="template.thumbnailUrl.startsWith('http') ? template.thumbnailUrl : `${apiBase}${template.thumbnailUrl}`"
                  :alt="template.name"
                  loading="lazy"
                />
                <div v-else class="template-thumbnail-placeholder flex align-items-center justify-content-center">
                  <i class="pi pi-globe text-4xl text-color-secondary"></i>
                </div>
                <Tag v-if="template.isFeatured" value="Featured" severity="warn" class="featured-badge" />
              </div>
            </template>
            <template #title>{{ template.name }}</template>
            <template #subtitle>
              <Tag v-if="template.category" :value="template.category" severity="secondary" class="text-xs mr-1" />
              <span class="text-color-secondary text-sm">{{ template.viewCount }} views</span>
            </template>
            <template #content>
              <p v-if="template.description" class="text-color-secondary text-sm mt-0 mb-3 line-clamp-2">
                {{ template.description }}
              </p>
              <p v-else-if="template.summary" class="text-color-secondary text-sm mt-0 mb-3 line-clamp-2">
                {{ template.summary }}
              </p>
              <Button
                label="Use Template"
                icon="pi pi-copy"
                class="w-full"
                :loading="isForkingId === template.id"
                :disabled="isForkingId !== null"
                @click="handleUseTemplate(template)"
              />
            </template>
          </Card>
        </div>
      </div>
    </template><!-- /v-if="activeTabIndex === 1" -->

    <!-- Create App Dialog -->
    <Dialog
      v-model:visible="showCreateDialog"
      modal
      header="Create New App"
      :style="{ width: '450px' }"
      :closable="!isCreating"
      :close-on-escape="!isCreating"
    >
      <div class="flex flex-column gap-3">
        <div class="flex flex-column gap-2">
          <label for="app-name" class="font-semibold">App Name</label>
          <InputText
            id="app-name"
            v-model="newAppName"
            placeholder="Enter app name"
            class="w-full"
            :disabled="isCreating"
            autofocus
            @keyup.enter="handleCreateApp"
          />
        </div>
      </div>
      <template #footer>
        <div class="flex justify-content-end gap-2">
          <Button
            label="Cancel"
            severity="secondary"
            outlined
            :disabled="isCreating"
            @click="showCreateDialog = false"
          />
          <Button label="Create" icon="pi pi-plus" :loading="isCreating" @click="handleCreateApp" />
        </div>
      </template>
    </Dialog>

    <!-- Publish App Dialog -->
    <Dialog
      v-model:visible="showPublishDialog"
      modal
      header="Publish App"
      :style="{ width: '500px' }"
      :closable="!isPublishing"
      :close-on-escape="!isPublishing"
    >
      <div class="flex flex-column gap-4">
        <p class="text-color-secondary m-0">
          Publishing will make "{{ publishingApp?.name }}" accessible to users via a public URL.
        </p>

        <div class="flex flex-column gap-2">
          <label for="public-url" class="font-semibold">Public URL</label>
          <div class="p-inputgroup">
            <span class="p-inputgroup-addon">/web/</span>
            <InputText
              id="public-url"
              v-model="publicUrl"
              placeholder="my-app"
              :class="{ 'p-invalid': urlError }"
              :disabled="isPublishing"
              @input="validateUrl"
            />
          </div>
          <small v-if="urlError" class="p-error">{{ urlError }}</small>
          <small v-else class="text-color-secondary">
            Your app will be accessible at /web/{{ publicUrl || 'your-slug' }}
          </small>
        </div>

        <div class="flex align-items-center gap-2">
          <input
            id="requires-auth"
            v-model="requiresAuth"
            type="checkbox"
            :disabled="isPublishing"
            class="p-checkbox"
          />
          <label for="requires-auth" class="cursor-pointer">
            Require authentication to access
          </label>
        </div>
      </div>
      <template #footer>
        <div class="flex justify-content-end gap-2">
          <Button
            label="Cancel"
            severity="secondary"
            outlined
            :disabled="isPublishing"
            @click="showPublishDialog = false"
          />
          <Button
            label="Publish"
            icon="pi pi-upload"
            :loading="isPublishing"
            @click="handlePublishApp"
          />
        </div>
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.apps-view {
  max-width: 1400px;
  margin: 0 auto;
}

.quick-start-card {
  /* stretch to fill available width — no max-width constraint */
}

.quick-start-header {
  display: flex;
  align-items: center;
}

/* ── Quick-start tabs ── */
.qs-tabs {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.qs-tab {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 7px 14px;
  border-radius: 6px;
  border: 1px solid var(--p-surface-border);
  background: transparent;
  color: var(--p-text-color);
  font-size: 0.82rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}

.qs-tab:hover {
  border-color: var(--p-primary-500);
  color: var(--p-primary-500);
}

.qs-tab--active {
  border-color: var(--p-primary-500);
  background: color-mix(in srgb, var(--p-primary-500) 10%, transparent);
  color: var(--p-primary-500);
}

/* ── Quick-start content ── */
.qs-content {
  /* no width cap — let it breathe inside the full-width card */
}

.qs-steps {
  color: var(--p-text-muted-color);
  font-size: 0.86rem;
  line-height: 1.7;
  margin: 0 0 12px;
  padding-left: 20px;
}

.qs-steps li { margin-bottom: 4px; }

/* Code blocks are always dark regardless of theme — same as every IDE/terminal */
.qs-code {
  background: #161b22;
  border: 1px solid #30363d;
  border-radius: 6px;
  padding: 8px 12px;
  font-family: var(--font-mono, monospace);
  font-size: 0.8rem;
  color: #8ef0a1;
  overflow-x: auto;
}

.qs-code--block {
  color: #e6edf3;
  line-height: 1.55;
  font-size: 0.78rem;
}

.qs-code--block em {
  color: #ffa657;
  font-style: normal;
}

.qs-inline-code {
  background: color-mix(in srgb, var(--p-primary-500) 12%, var(--p-surface-ground));
  color: var(--p-primary-400);
  padding: 1px 5px;
  border-radius: 4px;
  font-family: var(--font-mono, monospace);
  font-size: 0.83em;
}

.qs-links {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.qs-link,
.qs-inline-link {
  color: var(--p-primary-500);
  font-weight: 600;
}

.qs-link:hover,
.qs-inline-link:hover {
  color: var(--p-primary-400);
}

.qs-inline-link {
  background: none;
  border: 0;
  padding: 0;
  cursor: pointer;
  font: inherit;
  text-decoration: underline;
}

.template-card .template-thumbnail {
  position: relative;
  height: 160px;
  overflow: hidden;
  border-radius: var(--p-card-border-radius, 8px) var(--p-card-border-radius, 8px) 0 0;
}

.template-card .template-thumbnail img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.template-card .template-thumbnail-placeholder {
  width: 100%;
  height: 100%;
  background: var(--p-surface-100);
}

.featured-badge {
  position: absolute;
  top: 8px;
  right: 8px;
}

.line-clamp-2 {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
