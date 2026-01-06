<script setup lang="ts">
import { ref } from 'vue'

const tab = ref('general')
</script>

<template>
  <div>
    <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/projects" class="mb-4">
      Back to Projects
    </v-btn>

    <h1 class="text-h5 font-weight-bold mb-6">Project Settings</h1>

    <v-row>
      <v-col cols="12" md="3">
        <v-card>
          <v-list density="compact" nav>
            <v-list-item
              prepend-icon="mdi-cog"
              title="General"
              :active="tab === 'general'"
              @click="tab = 'general'"
            />
            <v-list-item
              prepend-icon="mdi-account-group"
              title="Members"
              :active="tab === 'members'"
              @click="tab = 'members'"
            />
            <v-list-item
              prepend-icon="mdi-folder"
              title="Resources"
              :active="tab === 'resources'"
              @click="tab = 'resources'"
            />
          </v-list>
        </v-card>
      </v-col>

      <v-col cols="12" md="9">
        <v-card>
          <v-card-title>
            {{ tab === 'general' ? 'Project Details' : tab === 'members' ? 'Team Members' : 'Project Resources' }}
          </v-card-title>
          <v-divider />
          <v-card-text>
            <template v-if="tab === 'general'">
              <v-text-field label="Project Name" model-value="Acme Corp" class="mb-4" />
              <v-textarea label="Description" rows="3" class="mb-4" />
              <v-btn color="primary">Save Changes</v-btn>
            </template>

            <template v-if="tab === 'members'">
              <div class="d-flex justify-end mb-4">
                <v-btn color="primary" prepend-icon="mdi-plus">Invite Member</v-btn>
              </div>
              <v-list>
                <v-list-item title="John Doe (You)" subtitle="john@acme.com">
                  <template #append>
                    <v-chip size="small" color="primary">Admin</v-chip>
                  </template>
                </v-list-item>
                <v-list-item title="Jane Smith" subtitle="jane@acme.com">
                  <template #append>
                    <v-select
                      :items="['Admin', 'Developer', 'Viewer']"
                      model-value="Developer"
                      density="compact"
                      hide-details
                      style="width: 120px"
                    />
                  </template>
                </v-list-item>
              </v-list>
            </template>

            <template v-if="tab === 'resources'">
              <p class="text-medium-emphasis">Project resources overview - to be implemented</p>
            </template>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </div>
</template>
