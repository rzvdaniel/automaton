<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import SelectButton from 'primevue/selectbutton'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import { useTheme } from '@/shared/composables/useTheme'

const { isDark, toggleDarkMode } = useTheme()

const activeSection = ref('account')

// Account settings
const displayName = ref('John Doe')
const email = ref('john@example.com')

// Theme settings
const themeOptions = ref([
  { label: 'Light', value: 'light', icon: 'pi pi-sun' },
  { label: 'Dark', value: 'dark', icon: 'pi pi-moon' },
  { label: 'System', value: 'system', icon: 'pi pi-desktop' },
])
const selectedTheme = ref(isDark.value ? 'dark' : 'light')

// API Keys mock data
const apiKeys = ref([
  { id: 1, name: 'Production API Key', key: 'ak_prod_****', scope: 'Execute', created: '2024-01-15', lastUsed: '5 min ago' },
  { id: 2, name: 'Runner Key', key: 'ak_runner_****', scope: 'Runner', created: '2024-01-10', lastUsed: '2 hours ago' },
])

// AI Keys mock data
const aiKeys = ref([
  { id: 1, provider: 'OpenAI', model: 'gpt-4', configured: true },
  { id: 2, provider: 'Anthropic', model: 'claude-3', configured: false },
  { id: 3, provider: 'Google', model: 'gemini-pro', configured: false },
])

const handleThemeChange = (value: string) => {
  if (value === 'dark' && !isDark.value) {
    toggleDarkMode()
  } else if (value === 'light' && isDark.value) {
    toggleDarkMode()
  }
}
</script>

<template>
  <div class="settings-view">
    <!-- Page Header -->
    <div class="mb-5">
      <h1 class="text-3xl font-bold m-0">Settings</h1>
      <p class="text-color-secondary mt-1 mb-0">Manage your account and preferences</p>
    </div>

    <div class="grid">
      <!-- Sidebar -->
      <div class="col-12 lg:col-3">
        <Card>
          <template #content>
            <div class="flex flex-column gap-1">
              <div class="text-color-secondary text-sm font-semibold mb-2 px-3">Profile</div>
              <Button
                label="Account"
                icon="pi pi-user"
                :text="activeSection !== 'account'"
                :class="{ 'surface-ground': activeSection === 'account' }"
                class="w-full justify-content-start"
                @click="activeSection = 'account'"
              />
              <Button
                label="API Keys"
                icon="pi pi-key"
                :text="activeSection !== 'apikeys'"
                :class="{ 'surface-ground': activeSection === 'apikeys' }"
                class="w-full justify-content-start"
                @click="activeSection = 'apikeys'"
              />
              <Button
                label="AI Keys"
                icon="pi pi-sparkles"
                :text="activeSection !== 'aikeys'"
                :class="{ 'surface-ground': activeSection === 'aikeys' }"
                class="w-full justify-content-start"
                @click="activeSection = 'aikeys'"
              />

              <div class="text-color-secondary text-sm font-semibold mb-2 mt-4 px-3">Preferences</div>
              <Button
                label="Appearance"
                icon="pi pi-palette"
                :text="activeSection !== 'appearance'"
                :class="{ 'surface-ground': activeSection === 'appearance' }"
                class="w-full justify-content-start"
                @click="activeSection = 'appearance'"
              />
              <Button
                label="Notifications"
                icon="pi pi-bell"
                :text="activeSection !== 'notifications'"
                :class="{ 'surface-ground': activeSection === 'notifications' }"
                class="w-full justify-content-start"
                @click="activeSection = 'notifications'"
              />
            </div>
          </template>
        </Card>
      </div>

      <!-- Content -->
      <div class="col-12 lg:col-9">
        <!-- Account Section -->
        <Card v-if="activeSection === 'account'">
          <template #title>Account Settings</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="flex flex-column gap-2">
                <label for="email" class="font-medium">Email</label>
                <InputText id="email" v-model="email" class="w-full" disabled />
              </div>

              <div class="flex flex-column gap-2">
                <label for="displayName" class="font-medium">Display Name</label>
                <InputText id="displayName" v-model="displayName" class="w-full" />
              </div>

              <div class="flex flex-column gap-2">
                <label class="font-medium">Password</label>
                <Button label="Change Password" outlined class="w-auto align-self-start" />
              </div>

              <div class="flex justify-content-end">
                <Button label="Save Changes" />
              </div>
            </div>
          </template>
        </Card>

        <!-- API Keys Section -->
        <Card v-if="activeSection === 'apikeys'">
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>API Keys</span>
              <Button label="Generate New Key" icon="pi pi-plus" size="small" />
            </div>
          </template>
          <template #subtitle>
            Manage API keys for runners, webhooks, and integrations
          </template>
          <template #content>
            <DataTable :value="apiKeys" class="p-datatable-sm">
              <Column field="name" header="Name"></Column>
              <Column field="key" header="Key">
                <template #body="{ data }">
                  <code class="surface-ground p-1 border-round">{{ data.key }}</code>
                </template>
              </Column>
              <Column field="scope" header="Scope">
                <template #body="{ data }">
                  <Tag :value="data.scope" severity="secondary" />
                </template>
              </Column>
              <Column field="lastUsed" header="Last Used"></Column>
              <Column header="Actions">
                <template #body>
                  <div class="flex gap-1">
                    <Button icon="pi pi-copy" text rounded size="small" severity="secondary" />
                    <Button icon="pi pi-trash" text rounded size="small" severity="danger" />
                  </div>
                </template>
              </Column>
            </DataTable>
          </template>
        </Card>

        <!-- AI Keys Section -->
        <Card v-if="activeSection === 'aikeys'">
          <template #title>AI Provider Keys</template>
          <template #subtitle>
            Configure API keys for AI-powered features
          </template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div
                v-for="provider in aiKeys"
                :key="provider.id"
                class="flex align-items-center justify-content-between p-3 surface-ground border-round"
              >
                <div class="flex align-items-center gap-3">
                  <div
                    class="flex align-items-center justify-content-center border-round-lg"
                    style="width: 40px; height: 40px; background-color: var(--p-primary-100)"
                  >
                    <i class="pi pi-sparkles" style="color: var(--p-primary-600)"></i>
                  </div>
                  <div>
                    <div class="font-semibold">{{ provider.provider }}</div>
                    <div class="text-color-secondary text-sm">{{ provider.model }}</div>
                  </div>
                </div>
                <div class="flex align-items-center gap-3">
                  <Tag
                    :value="provider.configured ? 'Configured' : 'Not Configured'"
                    :severity="provider.configured ? 'success' : 'secondary'"
                  />
                  <Button
                    :label="provider.configured ? 'Update' : 'Configure'"
                    size="small"
                    :outlined="provider.configured"
                  />
                </div>
              </div>
            </div>
          </template>
        </Card>

        <!-- Appearance Section -->
        <Card v-if="activeSection === 'appearance'">
          <template #title>Appearance</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div>
                <label class="font-medium block mb-3">Theme</label>
                <SelectButton
                  v-model="selectedTheme"
                  :options="themeOptions"
                  option-label="label"
                  option-value="value"
                  @change="handleThemeChange(selectedTheme)"
                >
                  <template #option="{ option }">
                    <i :class="option.icon" class="mr-2"></i>
                    <span>{{ option.label }}</span>
                  </template>
                </SelectButton>
              </div>
            </div>
          </template>
        </Card>

        <!-- Notifications Section -->
        <Card v-if="activeSection === 'notifications'">
          <template #title>Notification Preferences</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="flex align-items-center justify-content-between p-3 surface-ground border-round">
                <div>
                  <div class="font-medium">Email Notifications</div>
                  <div class="text-color-secondary text-sm">Receive emails for flow failures</div>
                </div>
                <Button label="Configure" outlined size="small" />
              </div>

              <div class="flex align-items-center justify-content-between p-3 surface-ground border-round">
                <div>
                  <div class="font-medium">Slack Notifications</div>
                  <div class="text-color-secondary text-sm">Send notifications to Slack channel</div>
                </div>
                <Button label="Connect" outlined size="small" />
              </div>

              <div class="flex align-items-center justify-content-between p-3 surface-ground border-round">
                <div>
                  <div class="font-medium">Webhook Notifications</div>
                  <div class="text-color-secondary text-sm">POST to custom endpoint</div>
                </div>
                <Button label="Configure" outlined size="small" />
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-view {
  max-width: 1200px;
  margin: 0 auto;
}
</style>
