<script setup lang="ts">
import { ref } from 'vue'
import { useTheme } from 'vuetify'

const theme = useTheme()
const activeTab = ref('account')

const accountSettings = ref({
  name: 'John Doe',
  email: 'john@example.com',
  company: 'Acme Corp'
})

const apiKeys = ref([
  { name: 'Production Key', key: 'ak_prod_***************', created: '2024-01-01', lastUsed: '2 hours ago' },
  { name: 'Development Key', key: 'ak_dev_***************', created: '2024-01-15', lastUsed: 'Never' }
])

const aiKeys = ref([
  { provider: 'OpenAI', key: 'sk-***************', model: 'gpt-4' },
  { provider: 'Anthropic', key: 'sk-***************', model: 'claude-3' }
])
</script>

<template>
  <div>
    <!-- Page Header -->
    <div class="mb-6">
      <h1 class="text-h4 font-weight-bold">Settings</h1>
      <p class="text-body-2 text-medium-emphasis">Manage your account and preferences</p>
    </div>

    <!-- Settings Layout: Sidebar | Content -->
    <v-row>
      <!-- Sidebar -->
      <v-col cols="12" md="3">
        <v-card>
          <v-list density="compact" nav>
            <v-list-item
              :active="activeTab === 'account'"
              prepend-icon="mdi-account"
              title="Account"
              @click="activeTab = 'account'"
            />
            <v-list-item
              :active="activeTab === 'api-keys'"
              prepend-icon="mdi-key"
              title="API Keys"
              @click="activeTab = 'api-keys'"
            />
            <v-list-item
              :active="activeTab === 'ai-keys'"
              prepend-icon="mdi-robot"
              title="AI API Keys"
              @click="activeTab = 'ai-keys'"
            />
            <v-list-item
              :active="activeTab === 'appearance'"
              prepend-icon="mdi-palette"
              title="Appearance"
              @click="activeTab = 'appearance'"
            />
            <v-list-item
              :active="activeTab === 'notifications'"
              prepend-icon="mdi-bell"
              title="Notifications"
              @click="activeTab = 'notifications'"
            />
          </v-list>
        </v-card>
      </v-col>

      <!-- Content -->
      <v-col cols="12" md="9">
        <!-- Account Settings -->
        <v-card v-if="activeTab === 'account'">
          <v-card-title>Account Settings</v-card-title>
          <v-divider />
          <v-card-text class="pa-6">
            <v-text-field
              v-model="accountSettings.name"
              label="Full Name"
              variant="outlined"
              class="mb-4"
            />
            <v-text-field
              v-model="accountSettings.email"
              label="Email"
              type="email"
              variant="outlined"
              class="mb-4"
            />
            <v-text-field
              v-model="accountSettings.company"
              label="Company"
              variant="outlined"
              class="mb-4"
            />

            <v-divider class="my-6" />

            <h3 class="text-subtitle-1 font-weight-bold mb-4">Password</h3>
            <v-btn variant="outlined">Change Password</v-btn>

            <v-divider class="my-6" />

            <v-btn color="primary">Save Changes</v-btn>
          </v-card-text>
        </v-card>

        <!-- API Keys -->
        <v-card v-if="activeTab === 'api-keys'">
          <v-card-title class="d-flex align-center justify-space-between">
            <span>API Keys</span>
            <v-btn variant="outlined" size="small" prepend-icon="mdi-plus">New Key</v-btn>
          </v-card-title>
          <v-divider />
          <v-card-text class="pa-6">
            <p class="text-body-2 text-medium-emphasis mb-4">
              API keys are used to authenticate runners and external integrations
            </p>
            <v-list>
              <v-list-item v-for="key in apiKeys" :key="key.name" class="mb-2">
                <v-list-item-title>{{ key.name }}</v-list-item-title>
                <v-list-item-subtitle>
                  <div>Key: <code class="text-body-2">{{ key.key }}</code></div>
                  <div class="mt-1">Created: {{ key.created }} • Last used: {{ key.lastUsed }}</div>
                </v-list-item-subtitle>
                <template #append>
                  <v-btn icon="mdi-content-copy" variant="text" size="small" class="mr-2" />
                  <v-btn icon="mdi-delete" variant="text" size="small" color="error" />
                </template>
              </v-list-item>
            </v-list>
          </v-card-text>
        </v-card>

        <!-- AI API Keys -->
        <v-card v-if="activeTab === 'ai-keys'">
          <v-card-title class="d-flex align-center justify-space-between">
            <span>AI API Keys</span>
            <v-btn variant="outlined" size="small" prepend-icon="mdi-plus">Add Provider</v-btn>
          </v-card-title>
          <v-divider />
          <v-card-text class="pa-6">
            <p class="text-body-2 text-medium-emphasis mb-4">
              Configure AI providers for flow generation
            </p>
            <v-list>
              <v-list-item v-for="key in aiKeys" :key="key.provider" class="mb-2">
                <v-list-item-title>{{ key.provider }}</v-list-item-title>
                <v-list-item-subtitle>
                  <div>Key: <code class="text-body-2">{{ key.key }}</code></div>
                  <div class="mt-1">Model: {{ key.model }}</div>
                </v-list-item-subtitle>
                <template #append>
                  <v-btn icon="mdi-pencil" variant="text" size="small" class="mr-2" />
                  <v-btn icon="mdi-delete" variant="text" size="small" color="error" />
                </template>
              </v-list-item>
            </v-list>
          </v-card-text>
        </v-card>

        <!-- Appearance -->
        <v-card v-if="activeTab === 'appearance'">
          <v-card-title>Appearance</v-card-title>
          <v-divider />
          <v-card-text class="pa-6">
            <h3 class="text-subtitle-1 font-weight-bold mb-4">Theme</h3>
            <v-btn-group variant="outlined">
              <v-btn :variant="theme.global.name.value === 'light' ? 'flat' : 'outlined'" @click="theme.global.name.value = 'light'">
                <v-icon>mdi-white-balance-sunny</v-icon>
                <span class="ml-2">Light</span>
              </v-btn>
              <v-btn :variant="theme.global.name.value === 'dark' ? 'flat' : 'outlined'" @click="theme.global.name.value = 'dark'">
                <v-icon>mdi-moon-waning-crescent</v-icon>
                <span class="ml-2">Dark</span>
              </v-btn>
            </v-btn-group>
          </v-card-text>
        </v-card>

        <!-- Notifications -->
        <v-card v-if="activeTab === 'notifications'">
          <v-card-title>Notification Preferences</v-card-title>
          <v-divider />
          <v-card-text class="pa-6">
            <v-switch
              label="Email notifications for flow failures"
              color="primary"
              hide-details
              class="mb-4"
            />
            <v-switch
              label="Email notifications for flow success"
              color="primary"
              hide-details
              class="mb-4"
            />
            <v-switch
              label="Weekly summary emails"
              color="primary"
              hide-details
              class="mb-4"
            />

            <v-divider class="my-6" />

            <v-btn color="primary">Save Preferences</v-btn>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
