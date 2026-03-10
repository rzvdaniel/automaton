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
  UserPlan,
} from '../types/admin.types'

const toast = useToast()

// ── State ────────────────────────────────────────────────────────────────────

const plans = ref<SubscriptionPlan[]>([])
const users = ref<UserPlan[]>([])
const loadingPlans = ref(false)
const loadingUsers = ref(false)

// Plan dialog
const planDialogVisible = ref(false)
const editingPlan = ref<SubscriptionPlan | null>(null)
const planForm = ref<CreateSubscriptionPlanRequest>({
  name: '',
  description: '',
  monthlyCostLimitUsd: 10,
  priceUsd: 0,
  stripeProductId: '',
  stripePriceId: '',
  isDefault: false,
  canCreateProjects: false,
  canCreateOrganizations: false,
  sortOrder: 0,
})
const savingPlan = ref(false)

// Assign dialog
const assignDialogVisible = ref(false)
const assigningUser = ref<UserPlan | null>(null)
const selectedPlanId = ref<string | null>(null)
const assigning = ref(false)

// ── Computed ─────────────────────────────────────────────────────────────────

const planOptions = computed(() => [
  { label: '— None (remove plan) —', value: null },
  ...plans.value.map((p) => ({ label: p.priceUsd > 0 ? `${p.name} ($${p.priceUsd}/mo)` : `${p.name} (Free)`, value: p.id })),
])

// ── Load ─────────────────────────────────────────────────────────────────────

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

// ── Plan CRUD ─────────────────────────────────────────────────────────────────

function openCreatePlan() {
  editingPlan.value = null
  planForm.value = {
    name: '', description: '',
    monthlyCostLimitUsd: 10,
    priceUsd: 0,
    stripeProductId: '',
    stripePriceId: '',
    isDefault: false,
    canCreateProjects: false,
    canCreateOrganizations: false,
    sortOrder: 0,
  }
  planDialogVisible.value = true
}

function openEditPlan(plan: SubscriptionPlan) {
  editingPlan.value = plan
  planForm.value = {
    name: plan.name,
    description: plan.description ?? '',
    monthlyCostLimitUsd: plan.monthlyCostLimitUsd,
    priceUsd: plan.priceUsd,
    stripeProductId: plan.stripeProductId ?? '',
    stripePriceId: plan.stripePriceId ?? '',
    isDefault: plan.isDefault,
    canCreateProjects: plan.canCreateProjects,
    canCreateOrganizations: plan.canCreateOrganizations,
    sortOrder: plan.sortOrder,
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
    loadPlans()
    loadUsers() // refresh limits shown in users table
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to save plan', life: 3000 })
  } finally {
    savingPlan.value = false
  }
}

async function deletePlan(plan: SubscriptionPlan) {
  try {
    await adminSubscriptionPlansService.deletePlan(plan.id)
    toast.add({ severity: 'success', summary: 'Deleted', detail: `Plan "${plan.name}" deleted`, life: 2000 })
    loadPlans()
    loadUsers()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to delete plan', life: 3000 })
  }
}

// ── Assign Plan ───────────────────────────────────────────────────────────────

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
    toast.add({
      severity: 'success',
      summary: 'Assigned',
      detail: `Plan assigned to ${assigningUser.value.email}`,
      life: 2000,
    })
    assignDialogVisible.value = false
    loadUsers()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to assign plan', life: 3000 })
  } finally {
    assigning.value = false
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function usagePercent(user: UserPlan) {
  if (!user.monthlyCostLimitUsd) return 0
  return Math.min(100, Math.round((user.currentMonthCostUsd / user.monthlyCostLimitUsd) * 100))
}

function usageSeverity(user: UserPlan): 'success' | 'warn' | 'danger' {
  const pct = usagePercent(user)
  if (pct >= 100) return 'danger'
  if (pct >= 80) return 'warn'
  return 'success'
}
</script>

<template>
  <div class="flex flex-column gap-4">
    <!-- Plans section -->
    <div>
      <div class="flex align-items-center justify-content-between mb-3">
        <div>
          <div class="text-xl font-semibold">Subscription Plans</div>
          <div class="text-color-secondary text-sm">Define tiers with monthly AI spending limits</div>
        </div>
        <Button label="New Plan" icon="pi pi-plus" size="small" @click="openCreatePlan" />
      </div>

      <DataTable :value="plans" :loading="loadingPlans" stripedRows>
        <Column field="name" header="Plan" />
        <Column field="description" header="Description" />
        <Column header="Price" style="width:110px">
          <template #body="{ data }">
            <span v-if="data.priceUsd > 0">${{ data.priceUsd.toFixed(2) }}/mo</span>
            <span v-else class="text-color-secondary">Free</span>
          </template>
        </Column>
        <Column header="AI Budget" style="width:120px">
          <template #body="{ data }">
            ${{ data.monthlyCostLimitUsd.toFixed(2) }}
          </template>
        </Column>
        <Column header="Features" style="width:160px">
          <template #body="{ data }">
            <div class="flex gap-1 flex-wrap">
              <Tag v-if="data.canCreateProjects"      value="Projects" severity="secondary" />
              <Tag v-if="data.canCreateOrganizations" value="Orgs"     severity="secondary" />
            </div>
          </template>
        </Column>
        <Column header="Default" style="width:90px">
          <template #body="{ data }">
            <Tag v-if="data.isDefault" value="Default" severity="info" />
          </template>
        </Column>
        <Column header="Sort" field="sortOrder" style="width:70px" />
        <Column header="" style="width:120px">
          <template #body="{ data }">
            <div class="flex gap-1">
              <Button icon="pi pi-pencil" text size="small" @click="openEditPlan(data)" />
              <Button icon="pi pi-trash" text size="small" severity="danger" @click="deletePlan(data)" />
            </div>
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Users section -->
    <div>
      <div class="mb-3">
        <div class="text-xl font-semibold">User Plans</div>
        <div class="text-color-secondary text-sm">Assign subscription plans to users</div>
      </div>

      <DataTable :value="users" :loading="loadingUsers" stripedRows :globalFilterFields="['email', 'name', 'planName']">
        <Column field="email" header="User" sortable />
        <Column header="Plan" sortable>
          <template #body="{ data }">
            <span v-if="data.planName">{{ data.planName }}</span>
            <span v-else class="text-color-secondary text-sm">Default (${{ data.monthlyCostLimitUsd.toFixed(2) }})</span>
          </template>
        </Column>
        <Column header="This Month" style="width:180px">
          <template #body="{ data }">
            <div class="flex align-items-center gap-2">
              <Tag
                :value="`$${data.currentMonthCostUsd.toFixed(4)} / $${data.monthlyCostLimitUsd.toFixed(2)}`"
                :severity="usageSeverity(data)"
              />
              <span class="text-color-secondary text-xs">{{ usagePercent(data) }}%</span>
            </div>
          </template>
        </Column>
        <Column header="" style="width:100px">
          <template #body="{ data }">
            <Button label="Assign" icon="pi pi-tag" text size="small" @click="openAssign(data)" />
          </template>
        </Column>
      </DataTable>
    </div>
  </div>

  <!-- Plan create/edit dialog -->
  <Dialog
    v-model:visible="planDialogVisible"
    :header="editingPlan ? 'Edit Plan' : 'New Plan'"
    modal
    style="width: 420px"
  >
    <div class="flex flex-column gap-3">
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Name *</label>
        <InputText v-model="planForm.name" placeholder="e.g. Pro, Free, Enterprise" class="w-full" />
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Description</label>
        <Textarea v-model="planForm.description" rows="2" class="w-full" />
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Subscription Price (USD/month)</label>
        <InputNumber
          v-model="planForm.priceUsd"
          mode="currency"
          currency="USD"
          :min="0"
          :max="100000"
          :minFractionDigits="2"
          class="w-full"
          placeholder="0.00 = Free"
        />
        <small class="text-color-secondary">What you charge the user per month.</small>
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Monthly AI Spend Budget (USD)</label>
        <InputNumber
          v-model="planForm.monthlyCostLimitUsd"
          mode="currency"
          currency="USD"
          :min="0"
          :max="100000"
          :minFractionDigits="2"
          class="w-full"
        />
        <small class="text-color-secondary">How much of that is passed through to AI providers.</small>
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Stripe Product ID</label>
        <InputText v-model="planForm.stripeProductId" placeholder="prod_..." class="w-full" />
        <small class="text-color-secondary">From your Stripe Dashboard → Products.</small>
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Stripe Price ID</label>
        <InputText v-model="planForm.stripePriceId" placeholder="price_..." class="w-full" />
        <small class="text-color-secondary">Recurring price linked to the product above.</small>
      </div>
      <div class="flex align-items-center gap-2">
        <Checkbox v-model="planForm.isDefault" :binary="true" inputId="isDefault" />
        <label for="isDefault" class="text-sm">Set as default plan for new users</label>
      </div>

      <div class="font-medium text-sm mt-1">Feature Access</div>
      <div class="flex flex-column gap-2 pl-1">
        <div class="flex align-items-center gap-2">
          <Checkbox v-model="planForm.canCreateProjects" :binary="true" inputId="canProjects" />
          <label for="canProjects" class="text-sm">Can create Projects</label>
        </div>
        <div class="flex align-items-center gap-2">
          <Checkbox v-model="planForm.canCreateOrganizations" :binary="true" inputId="canOrgs" />
          <label for="canOrgs" class="text-sm">Can create Organizations</label>
        </div>
      </div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Sort Order</label>
        <InputNumber v-model="planForm.sortOrder" :min="0" class="w-full" />
      </div>
    </div>
    <template #footer>
      <Button label="Cancel" text @click="planDialogVisible = false" />
      <Button
        :label="editingPlan ? 'Save' : 'Create'"
        icon="pi pi-check"
        :loading="savingPlan"
        :disabled="!planForm.name.trim()"
        @click="savePlan"
      />
    </template>
  </Dialog>

  <!-- Assign plan dialog -->
  <Dialog v-model:visible="assignDialogVisible" header="Assign Plan" modal style="width: 380px">
    <div v-if="assigningUser" class="flex flex-column gap-3">
      <div class="text-sm text-color-secondary">Assigning plan to: <strong>{{ assigningUser.email }}</strong></div>
      <div class="flex flex-column gap-1">
        <label class="font-medium text-sm">Subscription Plan</label>
        <Select
          v-model="selectedPlanId"
          :options="planOptions"
          optionLabel="label"
          optionValue="value"
          placeholder="Select a plan"
          class="w-full"
        />
      </div>
    </div>
    <template #footer>
      <Button label="Cancel" text @click="assignDialogVisible = false" />
      <Button label="Assign" icon="pi pi-check" :loading="assigning" @click="confirmAssign" />
    </template>
  </Dialog>
</template>
