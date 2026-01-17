<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Dropdown from 'primevue/dropdown'
import Tag from 'primevue/tag'

const projectName = ref('Acme Corp')
const projectDescription = ref('Main production project for Acme Corp workflows and automation')

const activeTab = ref(0)

// Mock members data
const members = ref([
  { id: 1, name: 'John Doe', email: 'john@acme.com', role: 'Admin', lastActive: '5 min ago', isYou: true },
  { id: 2, name: 'Jane Smith', email: 'jane@acme.com', role: 'Developer', lastActive: '2 hours ago', isYou: false },
  { id: 3, name: 'Bob Wilson', email: 'bob@acme.com', role: 'Viewer', lastActive: '1 day ago', isYou: false },
])

const pendingInvitations = ref([
  { id: 1, email: 'alice@acme.com', role: 'Developer', invitedAt: '2 days ago' },
])

const roleOptions = ref([
  { label: 'Admin', value: 'Admin' },
  { label: 'Developer', value: 'Developer' },
  { label: 'AppUser', value: 'AppUser' },
  { label: 'Viewer', value: 'Viewer' },
])

// Mock resources
const resources = ref({
  flows: [
    { name: 'Order Processing', icon: 'pi pi-bolt' },
    { name: 'Email Campaign', icon: 'pi pi-bolt' },
    { name: 'Data Sync', icon: 'pi pi-bolt' },
  ],
  apps: [
    { name: 'Customer Portal', icon: 'pi pi-globe' },
    { name: 'Admin Dashboard', icon: 'pi pi-globe' },
  ],
  runners: [
    { name: 'production-runner-01', status: 'online' },
    { name: 'staging-runner', status: 'online' },
  ],
  steps: [
    { name: 'ValidateEmail', icon: 'pi pi-puzzle-piece' },
    { name: 'SendSlackMsg', icon: 'pi pi-puzzle-piece' },
  ],
})
</script>

<template>
  <div class="project-settings-view">
    <!-- Header -->
    <div class="flex align-items-center gap-3 mb-5">
      <Button icon="pi pi-arrow-left" text rounded @click="$router.push('/projects')" />
      <div class="flex-1">
        <h1 class="text-2xl font-bold m-0">{{ projectName }}</h1>
        <p class="text-color-secondary mt-1 mb-0">Project Settings</p>
      </div>
      <Button label="Delete Project" severity="danger" outlined />
    </div>

    <div class="grid">
      <!-- Sidebar Navigation -->
      <div class="col-12 lg:col-3">
        <Card>
          <template #content>
            <div class="flex flex-column gap-1">
              <Button
                label="General"
                :text="activeTab !== 0"
                :class="{ 'bg-primary-50': activeTab === 0 }"
                class="w-full justify-content-start"
                @click="activeTab = 0"
              />
              <Button
                label="Members"
                :text="activeTab !== 1"
                :class="{ 'bg-primary-50': activeTab === 1 }"
                class="w-full justify-content-start"
                @click="activeTab = 1"
              />
              <Button
                label="Resources"
                :text="activeTab !== 2"
                :class="{ 'bg-primary-50': activeTab === 2 }"
                class="w-full justify-content-start"
                @click="activeTab = 2"
              />
            </div>
          </template>
        </Card>
      </div>

      <!-- Content Area -->
      <div class="col-12 lg:col-9">
        <!-- General Tab -->
        <Card v-if="activeTab === 0">
          <template #title>Project Details</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="flex flex-column gap-2">
                <label for="name" class="font-medium">Name</label>
                <InputText id="name" v-model="projectName" class="w-full" />
              </div>

              <div class="flex flex-column gap-2">
                <label for="description" class="font-medium">Description</label>
                <Textarea id="description" v-model="projectDescription" rows="4" class="w-full" />
              </div>

              <div class="flex justify-content-end">
                <Button label="Save Changes" />
              </div>
            </div>
          </template>
        </Card>

        <!-- Members Tab -->
        <div v-if="activeTab === 1">
          <Card class="mb-4">
            <template #title>
              <div class="flex align-items-center justify-content-between">
                <span>Team Members</span>
                <Button label="Invite" icon="pi pi-plus" size="small" />
              </div>
            </template>
            <template #content>
              <div class="flex flex-column gap-3">
                <div
                  v-for="member in members"
                  :key="member.id"
                  class="flex align-items-center justify-content-between p-3 surface-ground border-round"
                >
                  <div class="flex align-items-center gap-3">
                    <div
                      class="flex align-items-center justify-content-center border-round-lg surface-200"
                      style="width: 40px; height: 40px"
                    >
                      <i class="pi pi-user"></i>
                    </div>
                    <div>
                      <div class="font-medium">
                        {{ member.name }}
                        <Tag v-if="member.isYou" value="You" severity="info" class="ml-2" />
                      </div>
                      <div class="text-color-secondary text-sm">{{ member.email }}</div>
                      <div class="text-color-secondary text-xs">Last active: {{ member.lastActive }}</div>
                    </div>
                  </div>
                  <div class="flex align-items-center gap-2">
                    <Dropdown
                      :model-value="member.role"
                      :options="roleOptions"
                      option-label="label"
                      option-value="value"
                      class="w-8rem"
                    />
                    <Button icon="pi pi-ellipsis-v" text rounded severity="secondary" />
                  </div>
                </div>
              </div>
            </template>
          </Card>

          <Card>
            <template #title>Pending Invitations</template>
            <template #content>
              <div v-if="pendingInvitations.length > 0" class="flex flex-column gap-3">
                <div
                  v-for="invitation in pendingInvitations"
                  :key="invitation.id"
                  class="flex align-items-center justify-content-between p-3 surface-ground border-round"
                >
                  <div class="flex align-items-center gap-3">
                    <div
                      class="flex align-items-center justify-content-center border-round-lg surface-200"
                      style="width: 40px; height: 40px"
                    >
                      <i class="pi pi-envelope"></i>
                    </div>
                    <div>
                      <div class="font-medium">{{ invitation.email }}</div>
                      <div class="text-color-secondary text-sm">Invited {{ invitation.invitedAt }}</div>
                    </div>
                  </div>
                  <div class="flex align-items-center gap-2">
                    <Tag :value="invitation.role" severity="secondary" />
                    <Button label="Resend" text size="small" />
                    <Button icon="pi pi-times" text rounded severity="danger" size="small" />
                  </div>
                </div>
              </div>
              <div v-else class="text-color-secondary">No pending invitations</div>
            </template>
          </Card>

          <!-- Roles Info -->
          <Card class="mt-4">
            <template #title>Roles</template>
            <template #content>
              <ul class="m-0 pl-4 text-color-secondary">
                <li class="mb-2"><strong>Admin</strong> - Full control</li>
                <li class="mb-2"><strong>Developer</strong> - Create/edit flows, apps, steps</li>
                <li class="mb-2"><strong>AppUser</strong> - Execute apps only</li>
                <li><strong>Viewer</strong> - Read-only access</li>
              </ul>
            </template>
          </Card>
        </div>

        <!-- Resources Tab -->
        <Card v-if="activeTab === 2">
          <template #title>Project Resources</template>
          <template #content>
            <div class="flex flex-column gap-5">
              <!-- Flows -->
              <div>
                <div class="flex align-items-center justify-content-between mb-3">
                  <span class="font-semibold">Flows ({{ resources.flows.length }})</span>
                  <Button label="View All" link size="small" />
                </div>
                <div class="flex flex-column gap-2">
                  <div
                    v-for="flow in resources.flows"
                    :key="flow.name"
                    class="flex align-items-center gap-2 p-2 surface-ground border-round"
                  >
                    <i :class="flow.icon" class="text-color-secondary"></i>
                    <span>{{ flow.name }}</span>
                  </div>
                </div>
              </div>

              <!-- Apps -->
              <div>
                <div class="flex align-items-center justify-content-between mb-3">
                  <span class="font-semibold">Apps ({{ resources.apps.length }})</span>
                  <Button label="View All" link size="small" />
                </div>
                <div class="flex flex-column gap-2">
                  <div
                    v-for="app in resources.apps"
                    :key="app.name"
                    class="flex align-items-center gap-2 p-2 surface-ground border-round"
                  >
                    <i :class="app.icon" class="text-color-secondary"></i>
                    <span>{{ app.name }}</span>
                  </div>
                </div>
              </div>

              <!-- Runners -->
              <div>
                <div class="flex align-items-center justify-content-between mb-3">
                  <span class="font-semibold">Runners ({{ resources.runners.length }})</span>
                  <Button label="View All" link size="small" />
                </div>
                <div class="flex flex-column gap-2">
                  <div
                    v-for="runner in resources.runners"
                    :key="runner.name"
                    class="flex align-items-center gap-2 p-2 surface-ground border-round"
                  >
                    <div
                      class="w-0.5rem h-0.5rem border-round-lg"
                      :style="{ backgroundColor: runner.status === 'online' ? 'var(--p-green-500)' : 'var(--p-red-500)' }"
                    ></div>
                    <span>{{ runner.name }}</span>
                  </div>
                </div>
              </div>

              <!-- Steps -->
              <div>
                <div class="flex align-items-center justify-content-between mb-3">
                  <span class="font-semibold">Custom Steps ({{ resources.steps.length }})</span>
                  <Button label="View All" link size="small" />
                </div>
                <div class="flex flex-column gap-2">
                  <div
                    v-for="step in resources.steps"
                    :key="step.name"
                    class="flex align-items-center gap-2 p-2 surface-ground border-round"
                  >
                    <i :class="step.icon" class="text-color-secondary"></i>
                    <span>{{ step.name }}</span>
                  </div>
                </div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.project-settings-view {
  max-width: 1400px;
  margin: 0 auto;
}
</style>
