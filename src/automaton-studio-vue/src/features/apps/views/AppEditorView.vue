<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tree from 'primevue/tree'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import Skeleton from 'primevue/skeleton'
import InputText from 'primevue/inputtext'
import SelectButton from 'primevue/selectbutton'

const appName = ref('My Web App')

// File tree mock data
const files = ref([
  {
    key: 'src',
    label: 'src',
    icon: 'pi pi-folder',
    children: [
      { key: 'index', label: 'index.html', icon: 'pi pi-file' },
      { key: 'styles', label: 'styles.css', icon: 'pi pi-file' },
      { key: 'app', label: 'app.js', icon: 'pi pi-file' },
    ],
  },
  {
    key: 'assets',
    label: 'assets',
    icon: 'pi pi-folder',
    children: [{ key: 'logo', label: 'logo.svg', icon: 'pi pi-image' }],
  },
  {
    key: 'backend',
    label: 'Backend',
    icon: 'pi pi-folder',
    children: [
      { key: 'getUsers', label: 'getUsersStep', icon: 'pi pi-puzzle-piece' },
      { key: 'saveData', label: 'saveDataStep', icon: 'pi pi-puzzle-piece' },
    ],
  },
])

const selectedFile = ref<Record<string, boolean>>({})
const openTabs = ref(['index.html', 'styles.css'])
const activeTab = ref(0)

// Preview mode
const previewMode = ref('desktop')
const previewModes = ref([
  { icon: 'pi pi-desktop', value: 'desktop' },
  { icon: 'pi pi-tablet', value: 'tablet' },
  { icon: 'pi pi-mobile', value: 'mobile' },
])

const aiPrompt = ref('')
</script>

<template>
  <div class="app-editor-view">
    <!-- Header -->
    <div class="flex align-items-center justify-content-between mb-4">
      <div class="flex align-items-center gap-3">
        <Button icon="pi pi-arrow-left" text rounded @click="$router.push('/apps')" />
        <div class="flex align-items-center gap-2">
          <i class="pi pi-globe" style="color: var(--p-primary-500)"></i>
          <InputText v-model="appName" class="p-inputtext-lg font-semibold" />
        </div>
      </div>
      <div class="flex align-items-center gap-2">
        <Button label="Preview" icon="pi pi-eye" outlined severity="secondary" />
        <Button label="Publish" icon="pi pi-upload" />
        <Button icon="pi pi-ellipsis-v" text rounded severity="secondary" />
      </div>
    </div>

    <!-- Main Editor Layout -->
    <div class="editor-layout">
      <!-- File Tree Panel -->
      <div class="file-panel">
        <div class="font-semibold mb-3 px-2">Files</div>
        <Tree :value="files" selection-mode="single" v-model:selection-keys="selectedFile" class="w-full border-none p-0" />
        <Button label="+ New File" text class="w-full mt-3" size="small" />
      </div>

      <!-- Editor Panel -->
      <div class="editor-panel">
        <TabView v-model:active-index="activeTab" class="editor-tabs">
          <TabPanel v-for="tab in openTabs" :key="tab" :value="tab" :header="tab">
            <div class="code-editor-placeholder">
              <Skeleton width="100%" height="20px" class="mb-2" />
              <Skeleton width="80%" height="20px" class="mb-2" />
              <Skeleton width="90%" height="20px" class="mb-2" />
              <Skeleton width="60%" height="20px" class="mb-2" />
              <Skeleton width="85%" height="20px" class="mb-2" />
              <Skeleton width="70%" height="20px" class="mb-2" />
              <Skeleton width="95%" height="20px" class="mb-2" />
              <Skeleton width="50%" height="20px" class="mb-2" />
              <Skeleton width="75%" height="20px" class="mb-2" />
              <Skeleton width="65%" height="20px" class="mb-2" />
              <div class="text-center text-color-secondary mt-4">
                <i class="pi pi-code text-4xl mb-2"></i>
                <p>Monaco Editor will be integrated here</p>
              </div>
            </div>
          </TabPanel>
        </TabView>
      </div>

      <!-- Preview Panel -->
      <div class="preview-panel">
        <div class="flex align-items-center justify-content-between mb-3 px-2">
          <span class="font-semibold">Preview</span>
          <SelectButton v-model="previewMode" :options="previewModes" option-value="value" size="small">
            <template #option="{ option }">
              <i :class="option.icon"></i>
            </template>
          </SelectButton>
        </div>
        <div class="preview-frame" :class="previewMode">
          <div class="preview-content">
            <Skeleton width="100%" height="40px" class="mb-3" />
            <Skeleton width="60%" height="24px" class="mb-2" />
            <Skeleton width="80%" height="16px" class="mb-4" />
            <div class="grid">
              <div class="col-6">
                <Skeleton width="100%" height="100px" />
              </div>
              <div class="col-6">
                <Skeleton width="100%" height="100px" />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- AI Assistant Bar -->
    <Card class="mt-4">
      <template #content>
        <div class="flex align-items-center gap-3">
          <div class="flex align-items-center gap-2 flex-shrink-0">
            <i class="pi pi-sparkles" style="color: var(--p-primary-500)"></i>
            <span class="font-semibold">AI Assistant</span>
          </div>
          <InputText
            v-model="aiPrompt"
            placeholder="Add a login form with email and password fields..."
            class="flex-1"
          />
          <Button label="Go" icon="pi pi-send" :disabled="!aiPrompt" />
          <Button label="Expand" icon="pi pi-window-maximize" text severity="secondary" />
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.app-editor-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 64px - 3rem);
}

.editor-layout {
  display: flex;
  gap: 1rem;
  flex: 1;
  min-height: 0;
}

.file-panel {
  width: 200px;
  flex-shrink: 0;
  background-color: var(--p-surface-0);
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  padding: 1rem 0.5rem;
  overflow: auto;
}

.editor-panel {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.editor-tabs {
  flex: 1;
  display: flex;
  flex-direction: column;
}

.editor-tabs :deep(.p-tabview-panels) {
  flex: 1;
  padding: 0;
}

.editor-tabs :deep(.p-tabview-panel) {
  height: 100%;
}

.code-editor-placeholder {
  height: 100%;
  background-color: var(--p-surface-0);
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  padding: 1rem;
  font-family: 'JetBrains Mono', monospace;
}

.preview-panel {
  width: 350px;
  flex-shrink: 0;
}

.preview-frame {
  background-color: var(--p-surface-0);
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  height: calc(100% - 40px);
  overflow: auto;
  transition: width 0.2s ease;
}

.preview-frame.desktop {
  width: 100%;
}

.preview-frame.tablet {
  width: 280px;
  margin: 0 auto;
}

.preview-frame.mobile {
  width: 180px;
  margin: 0 auto;
}

.preview-content {
  padding: 1rem;
}

:global(.app-dark) .file-panel,
:global(.app-dark) .code-editor-placeholder,
:global(.app-dark) .preview-frame {
  background-color: var(--p-surface-900);
  border-color: var(--p-surface-700);
}
</style>
