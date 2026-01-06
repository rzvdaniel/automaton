<script setup lang="ts">
import { ref } from 'vue'

const activeTab = ref('index.html')
const tabs = ref(['index.html', 'styles.css', 'app.js'])
const fileTree = ref([
  { name: 'src', type: 'folder', children: [
    { name: 'index.html', type: 'file' },
    { name: 'styles.css', type: 'file' },
    { name: 'app.js', type: 'file' }
  ]},
  { name: 'assets', type: 'folder', children: [
    { name: 'logo.png', type: 'file' }
  ]},
  { name: 'Backend', type: 'folder', children: [
    { name: 'steps', type: 'folder', children: [
      { name: 'getUser', type: 'step' },
      { name: 'saveData', type: 'step' }
    ]}
  ]}
])

const previewMode = ref('desktop')
const aiPrompt = ref('')
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="d-flex align-center justify-space-between mb-4">
      <div class="d-flex align-center">
        <v-btn icon="mdi-arrow-left" variant="text" class="mr-2" />
        <div>
          <h1 class="text-h5 font-weight-bold">My Web App</h1>
        </div>
      </div>
      <div class="d-flex gap-2">
        <v-btn variant="outlined">Preview</v-btn>
        <v-btn variant="outlined">Publish</v-btn>
        <v-btn icon="mdi-dots-vertical" variant="text" />
      </div>
    </div>

    <!-- Three-Panel Layout -->
    <v-card>
      <v-row no-gutters style="height: calc(100vh - 200px)">
        <!-- File Tree (Left Panel) -->
        <v-col cols="2" class="border-e">
          <v-card-title class="text-subtitle-2">Files</v-card-title>
          <v-divider />
          <v-card-text class="pa-2">
            <v-treeview
              :items="fileTree"
              item-title="name"
              item-value="name"
              density="compact"
            >
              <template #prepend="{ item }">
                <v-icon v-if="item.type === 'folder'" size="small">mdi-folder</v-icon>
                <v-icon v-else-if="item.type === 'file'" size="small">mdi-file</v-icon>
                <v-icon v-else-if="item.type === 'step'" size="small">mdi-puzzle</v-icon>
              </template>
            </v-treeview>
            <v-divider class="my-2" />
            <div class="pa-2">
              <div class="text-caption font-weight-bold mb-1">Backend</div>
              <v-list density="compact">
                <v-list-item prepend-icon="mdi-puzzle" title="getUser" density="compact" />
                <v-list-item prepend-icon="mdi-puzzle" title="saveData" density="compact" />
              </v-list>
            </div>
            <v-btn block variant="outlined" size="small" prepend-icon="mdi-plus" class="mt-2">
              New
            </v-btn>
          </v-card-text>
        </v-col>

        <!-- Editor (Center Panel) -->
        <v-col cols="6" class="border-e">
          <v-tabs v-model="activeTab" density="compact">
            <v-tab v-for="tab in tabs" :key="tab" :value="tab">
              {{ tab }}
              <v-btn icon="mdi-close" variant="text" size="x-small" class="ml-2" />
            </v-tab>
          </v-tabs>
          <v-divider />
          <v-card-text class="pa-0">
            <v-sheet color="surface-variant" height="100%" class="pa-4">
              <pre class="text-body-2"><code>&lt;!DOCTYPE html&gt;
&lt;html lang="en"&gt;
&lt;head&gt;
  &lt;title&gt;My App&lt;/title&gt;
  &lt;link rel="stylesheet" href="styles.css"&gt;
&lt;/head&gt;
&lt;body&gt;
  &lt;div id="app"&gt;
    &lt;h1&gt;Hello World&lt;/h1&gt;
  &lt;/div&gt;
  &lt;script src="app.js"&gt;&lt;/script&gt;
&lt;/body&gt;
&lt;/html&gt;</code></pre>
            </v-sheet>
          </v-card-text>
        </v-col>

        <!-- Preview (Right Panel) -->
        <v-col cols="4">
          <v-card-title class="text-subtitle-2 d-flex align-center justify-space-between">
            <span>Preview</span>
            <v-btn-group variant="outlined" density="compact" divided>
              <v-btn size="x-small" :variant="previewMode === 'desktop' ? 'flat' : 'outlined'" @click="previewMode = 'desktop'">
                <v-icon size="small">mdi-monitor</v-icon>
              </v-btn>
              <v-btn size="x-small" :variant="previewMode === 'tablet' ? 'flat' : 'outlined'" @click="previewMode = 'tablet'">
                <v-icon size="small">mdi-tablet</v-icon>
              </v-btn>
              <v-btn size="x-small" :variant="previewMode === 'mobile' ? 'flat' : 'outlined'" @click="previewMode = 'mobile'">
                <v-icon size="small">mdi-cellphone</v-icon>
              </v-btn>
            </v-btn-group>
          </v-card-title>
          <v-divider />
          <v-card-text class="d-flex align-center justify-center pa-4" style="height: 100%">
            <v-sheet 
              :width="previewMode === 'desktop' ? '100%' : previewMode === 'tablet' ? '768px' : '375px'"
              height="100%"
              border
              class="pa-4"
            >
              <h1>Hello World</h1>
              <p>Live preview will appear here</p>
            </v-sheet>
          </v-card-text>
        </v-col>
      </v-row>
    </v-card>

    <!-- AI Assistant (Bottom Panel - Collapsible) -->
    <v-card class="mt-4">
      <v-card-title class="text-subtitle-2 d-flex align-center justify-space-between">
        <div class="d-flex align-center">
          <v-icon class="mr-2">mdi-robot</v-icon>
          AI Assistant
        </div>
        <v-btn icon="mdi-chevron-up" variant="text" size="small" />
      </v-card-title>
      <v-divider />
      <v-card-text>
        <v-text-field
          v-model="aiPrompt"
          variant="outlined"
          placeholder="Add a login form with email and password fields"
          density="compact"
          append-inner-icon="mdi-send"
          hide-details
        />
      </v-card-text>
    </v-card>
  </div>
</template>
