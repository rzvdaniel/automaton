<script setup lang="ts">
import { ref } from 'vue'

const tab = ref('account')
</script>

<template>
  <div>
    <h1 class="text-h5 font-weight-bold mb-6">Settings</h1>

    <v-row>
      <v-col cols="12" md="3">
        <v-card>
          <v-list density="compact" nav>
            <v-list-subheader>Profile</v-list-subheader>
            <v-list-item
              prepend-icon="mdi-account"
              title="Account"
              :active="tab === 'account'"
              @click="tab = 'account'"
            />
            <v-list-item
              prepend-icon="mdi-key"
              title="API Keys"
              :active="tab === 'apikeys'"
              @click="tab = 'apikeys'"
            />
            <v-list-item
              prepend-icon="mdi-robot"
              title="AI Keys"
              :active="tab === 'aikeys'"
              @click="tab = 'aikeys'"
            />

            <v-list-subheader>Preferences</v-list-subheader>
            <v-list-item
              prepend-icon="mdi-palette"
              title="Appearance"
              :active="tab === 'appearance'"
              @click="tab = 'appearance'"
            />
            <v-list-item
              prepend-icon="mdi-bell"
              title="Notifications"
              :active="tab === 'notifications'"
              @click="tab = 'notifications'"
            />
          </v-list>
        </v-card>
      </v-col>

      <v-col cols="12" md="9">
        <v-card>
          <v-card-title>
            {{
              tab === 'account' ? 'Account Settings' :
              tab === 'apikeys' ? 'API Keys' :
              tab === 'aikeys' ? 'AI Provider Keys' :
              tab === 'appearance' ? 'Appearance' :
              'Notifications'
            }}
          </v-card-title>
          <v-divider />
          <v-card-text>
            <template v-if="tab === 'account'">
              <v-text-field label="Email" model-value="user@example.com" class="mb-4" readonly />
              <v-text-field label="Display Name" model-value="John Doe" class="mb-4" />
              <v-btn variant="outlined" class="mb-4">Change Password</v-btn>
              <v-divider class="my-4" />
              <v-btn color="primary">Save Changes</v-btn>
            </template>

            <template v-if="tab === 'apikeys'">
              <p class="text-medium-emphasis mb-4">Manage API keys for runners and integrations.</p>
              <v-btn color="primary" prepend-icon="mdi-plus">Generate New Key</v-btn>
            </template>

            <template v-if="tab === 'aikeys'">
              <p class="text-medium-emphasis mb-4">Configure AI provider API keys for flow generation.</p>
              <v-text-field label="OpenAI API Key" type="password" class="mb-4" />
              <v-text-field label="Anthropic API Key" type="password" class="mb-4" />
              <v-btn color="primary">Save Keys</v-btn>
            </template>

            <template v-if="tab === 'appearance'">
              <v-switch label="Dark Mode" />
            </template>

            <template v-if="tab === 'notifications'">
              <v-switch label="Email notifications" />
              <v-switch label="Flow execution alerts" />
            </template>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
