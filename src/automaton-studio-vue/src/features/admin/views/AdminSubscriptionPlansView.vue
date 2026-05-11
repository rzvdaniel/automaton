<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { adminSubscriptionPlansService } from '../services/adminSubscriptionPlansService'
import type {
  SubscriptionPlan,
  CreateSubscriptionPlanRequest,
  HostedRunnerCapability,
  UserPlan,
} from '../types/admin.types'

const toast = useToast()

const plans = ref<SubscriptionPlan[]>([])
const users = ref<UserPlan[]>([])
const loadingPlans = ref(false)
const loadingUsers = ref(false)

const planDialogVisible = ref(false)
const editingPlan = ref<SubscriptionPlan | null>(null)
const savingPlan = ref(false)

const assignDialogVisible = ref(false)
const assigningUser = ref<UserPlan | null>(null)
const selectedPlanId = ref<string | null>(null)
const assigning = ref(false)

const visibilityOptions = [
  { label: 'Disabled', value: 'disabled' },
  { label: 'Test users only', value: 'testUsersOnly' },
  { label: 'Public', value: 'public' },
]

function defaultHostedRunner(): HostedRunnerCapability {
  return {
    enabled: false,
    cpuCores: 0.5,
    memoryMb: 768,
    diskMb: 2048,
    maxApps: 10,
    visibility: 'disabled',
  }
}

function defaultPlanForm(): CreateSubscriptionPlanRequest {
  return {
    name: '',
    description: '',
    priceUsd: 0,
    stripeProductId: '',
    stripePriceId: '',
    isDefault: false,
    isActive: true,
    sortOrder: 0,
    capabilities: {
      version: 1,
      hostedRunner: defaultHostedRunner(),
    },
  }
}

const planForm = ref<CreateSubscriptionPlanRequest>(defaultPlanForm())

const planOptions = computed(() => [
  { label: 'None', value: null },
  ...plans.value
    .filter((plan) => plan.isActive)
    .map((plan) => ({ label: plan.priceUsd > 0 ? `${plan.name} ($${plan.priceUsd}/mo)` : `${plan.name} (Free)`, value: plan.id })),
])

async function loadPlans() {
  loadingPlans.value = true
  try {
    plans.value = await adminSubscriptionPlansService.getPlans()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load plans', life: 3000 })
  } finally {
    loadingPlans.value = false
  }
}

async function loadUsers() {
  loadingUsers.value = true
  try {
    users.value = await adminSubscriptionPlansService.getUsers()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load users', life: 3000 })
  } finally {
    loadingUsers.value = false
  }
}

onMounted(() => {
  loadPlans()
  loadUsers()
})

function openCreatePlan() {
  editingPlan.value = null
  planForm.value = defaultPlanForm()
  planDialogVisible.value = true
}

function openEditPlan(plan: SubscriptionPlan) {
  editingPlan.value = plan
  planForm.value = {
    name: plan.name,
    description: plan.description ?? '',
    priceUsd: plan.priceUsd,
    stripeProductId: plan.stripeProductId ?? '',
    stripePriceId: plan.stripePriceId ?? '',
    isDefault: plan.isDefault,
    isActive: plan.isActive,
    sortOrder: plan.sortOrder,
    capabilities: {
      version: plan.capabilities?.version ?? 1,
      hostedRunner: {
        ...defaultHostedRunner(),
        ...(plan.capabilities?.hostedRunner ?? {}),
      },
    },
  }
  planDialogVisible.value = true
}

async function savePlan() {
  if (!planForm.value.name.trim()) return
  savingPlan.value = true
  try {
    if (editingPlan.value) {
      await adminSubscriptionPlansService.updatePlan(editingPlan.value.id, planForm.value)
      toast.add({ severity: 'success', summary: 'Updated', detail: 'Plan updated', life: 2000 })
    } else {
      await adminSubscriptionPlansService.createPlan(planForm.value)
      toast.add({ severity: 'success', summary: 'Created', detail: 'Plan created', life: 2000 })
    }
    planDialogVisible.value = false
    await Promise.all([loadPlans(), loadUsers()])
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to save plan', life: 3000 })
  } finally {
    savingPlan.value = false
  }
}

async function togglePlanActive(plan: SubscriptionPlan) {
  try {
    await adminSubscriptionPlansService.updatePlan(plan.id, { isActive: !plan.isActive })
    toast.add({
      severity: 'success',
      summary: plan.isActive ? 'Plan disabled' : 'Plan enabled',
      detail: `Plan "${plan.name}" is now ${plan.isActive ? 'inactive' : 'active'}.`,
      life: 2000,
    })
    await Promise.all([loadPlans(), loadUsers()])
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to update plan status', life: 3000 })
  }
}

function openAssign(user: UserPlan) {
  assigningUser.value = user
  selectedPlanId.value = user.subscriptionPlanId ?? null
  assignDialogVisible.value = true
}

async function confirmAssign() {
  if (!assigningUser.value) return
  assigning.value = true
  try {
    await adminSubscriptionPlansService.assignPlan(assigningUser.value.userId, {
      planId: selectedPlanId.value ?? undefined,
    })
    toast.add({ severity: 'success', summary: 'Assigned', detail: `Plan assigned to ${assigningUser.value.email}`, life: 2000 })
    assignDialogVisible.value = false
    await loadUsers()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to assign plan', life: 3000 })
  } finally {
    assigning.value = false
  }
}

function runnerSummary(plan: SubscriptionPlan) {
  const runner = plan.capabilities.hostedRunner
  if (!runner.enabled) return 'No hosted runner'
  return `${runner.cpuCores} CPU, ${runner.memoryMb} MB, ${runner.diskMb} MB disk, ${runner.maxApps} apps`
}
</script>

<template>
  <div class="flex flex-column gap-4">
    <div>
      <div class="flex align-items-center justify-content-between mb-3">
        <div>
          <div class="text-xl font-semibold">Subscription Plans</div>
          <div class="text-color-secondary text-sm">Define Stripe plans and the hosted runner resources each plan includes.</div>
        </div>
        <Button label="New Plan" icon="pi pi-plus" size="small" @click="openCreatePlan" />
      </div>

      <DataTable :value="plans" :loading="loadingPlans">
        <Column field="name" header="Plan" />
        <Column field="description" header="Description" />
        <Column header="Price" style="width: 110px">
          <template #body="{ data }">
            <span v-if="data.priceUsd > 0">${{ data.priceUsd.toFixed(2) }}/mo</span>
            <span v-else class="text-color-secondary">Free</span>
          </template>
        </Column>
        <Column header="Hosted Runner">
          <template #body="{ data }">
            <span>{{ runnerSummary(data) }}</span>
          </template>
        </Column>
        <Column header="Visibility" style="width: 130px">
          <template #body="{ data }">
            <Tag :value="data.capabilities.hostedRunner.visibility" severity="secondary" />
          </template>
        </Column>
        <Column header="Default" style="width: 90px">
          <template #body="{ data }">
            <Tag v-if="data.isDefault" value="Default" severity="info" />
          </template>
        </Column>
        <Column header="Status" style="width: 100px">
          <template #body="{ data }">
            <Tag :value="data.isActive ? 'Active' : 'Inactive'" :severity="data.isActive ? 'success' : 'secondary'" />
          </template>
        </Column>
        <Column header="" style="width: 120px">
          <template #body="{ data }">
            <div class="flex gap-1">
              <Button icon="pi pi-pencil" text size="small" @click="openEditPlan(data)" />
              <Button
                :icon="data.isActive ? 'pi pi-eye' : 'pi pi-eye-slash'"
                text
                size="small"
                :severity="data.isActive ? 'secondary' : 'success'"
                :title="data.isActive ? 'Disable plan' : 'Enable plan'"
                @click="togglePlanActive(data)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </div>

    <div>
      <div class="mb-3">
        <div class="text-xl font-semibold">User Plans</div>
        <div class="text-color-secondary text-sm">Assign subscription plans to users while testing Stripe and hosted runners.</div>
      </div>

      <DataTable :value="users" :loading="loadingUsers" :global-filter-fields="['email', 'name', 'planName']">
        <Column field="email" header="User" sortable />
        <Column header="Plan" sortable>
          <template #body="{ data }">
            <span v-if="data.planName">{{ data.planName }}</span>
            <span v-else class="text-color-secondary text-sm">None</span>
          </template>
        </Column>
        <Column header="" style="width: 100px">
          <template #body="{ data }">
            <Button label="Assign" icon="pi pi-tag" text size="small" @click="openAssign(data)" />
          </template>
        </Column>
      </DataTable>
    </div>
  </div>

  <Dialog v-model:visible="planDialogVisible" :header="editingPlan ? 'Edit Plan' : 'New Plan'" modal style="width: 480px">
    <div class="flex flex-column gap-3">
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Name *</label>
        <InputText v-model="planForm.name" placeholder="e.g. Hosted Runner Small" class="w-full" />
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Description</label>
        <Textarea v-model="planForm.description" rows="2" class="w-full" />
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Subscription Price (USD/month)</label>
        <InputNumber v-model="planForm.priceUsd" mode="currency" currency="USD" :min="0" :max="100000" :min-fraction-digits="2" class="w-full" />
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Stripe Product ID</label>
        <InputText v-model="planForm.stripeProductId" placeholder="prod_..." class="w-full" />
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Stripe Price ID</label>
        <InputText v-model="planForm.stripePriceId" placeholder="price_..." class="w-full" />
      </div>

      <div class="font-medium text-sm mt-1">Hosted Runner</div>
      <div class="flex align-items-center gap-2">
        <Checkbox v-model="planForm.capabilities.hostedRunner.enabled" :binary="true" input-id="runnerEnabled" />
        <label for="runnerEnabled" class="text-sm">Includes hosted runner</label>
      </div>
      <div class="grid">
        <div class="col-6 flex flex-column gap-1">
          <label class="font-medium text-sm">CPU Cores</label>
          <InputNumber v-model="planForm.capabilities.hostedRunner.cpuCores" :min="0.1" :max="16" :min-fraction-digits="1" :max-fraction-digits="2" class="w-full" />
        </div>
        <div class="col-6 flex flex-column gap-1">
          <label class="font-medium text-sm">Memory MB</label>
          <InputNumber v-model="planForm.capabilities.hostedRunner.memoryMb" :min="128" :max="65536" class="w-full" />
        </div>
        <div class="col-6 flex flex-column gap-1">
          <label class="font-medium text-sm">Disk MB</label>
          <InputNumber v-model="planForm.capabilities.hostedRunner.diskMb" :min="256" :max="1048576" class="w-full" />
        </div>
        <div class="col-6 flex flex-column gap-1">
          <label class="font-medium text-sm">Max Apps</label>
          <InputNumber v-model="planForm.capabilities.hostedRunner.maxApps" :min="1" :max="1000" class="w-full" />
        </div>
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Visibility</label>
        <Select v-model="planForm.capabilities.hostedRunner.visibility" :options="visibilityOptions" option-label="label" option-value="value" class="w-full" />
      </div>

      <div class="flex align-items-center gap-2">
        <Checkbox v-model="planForm.isDefault" :binary="true" input-id="isDefault" />
        <label for="isDefault" class="text-sm">Set as default plan for new users</label>
      </div>
      <div class="flex align-items-center gap-2">
        <Checkbox v-model="planForm.isActive" :binary="true" input-id="isActive" />
        <label for="isActive" class="text-sm">Plan is active and visible to eligible users</label>
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Sort Order</label>
        <InputNumber v-model="planForm.sortOrder" :min="0" class="w-full" />
      </div>
    </div>
    <template #footer>
      <Button label="Cancel" text @click="planDialogVisible = false" />
      <Button :label="editingPlan ? 'Save' : 'Create'" icon="pi pi-check" :loading="savingPlan" :disabled="!planForm.name.trim()" @click="savePlan" />
    </template>
  </Dialog>

  <Dialog v-model:visible="assignDialogVisible" header="Assign Plan" modal style="width: 380px">
    <div v-if="assigningUser" class="flex flex-column gap-3">
      <div class="text-sm text-color-secondary">Assigning plan to: <strong>{{ assigningUser.email }}</strong></div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Subscription Plan</label>
        <Select v-model="selectedPlanId" :options="planOptions" option-label="label" option-value="value" placeholder="Select a plan" class="w-full" />
      </div>
    </div>
    <template #footer>
      <Button label="Cancel" text @click="assignDialogVisible = false" />
      <Button label="Assign" icon="pi pi-check" :loading="assigning" @click="confirmAssign" />
    </template>
  </Dialog>
</template>
