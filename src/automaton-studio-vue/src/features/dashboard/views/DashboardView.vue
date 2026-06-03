<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import StandardDashboard from '../components/StandardDashboard.vue'
import AppUserDashboard from '../components/AppUserDashboard.vue'
import PendingInvitationsCard from '@/features/organizations/components/PendingInvitationsCard.vue'

const { isAppUserOnly, loadOrganizations, organizations } = useOrganizationRole()
const route = useRoute()
const router = useRouter()
const toast = useToast()

// Check if user is an AppUser with organizations
const hasOrganizations = computed(() => organizations.value.length > 0)
const isAppUserDashboard = computed(() => isAppUserOnly.value && hasOrganizations.value)

function showEmailConfirmationFeedback() {
  if (route.query.emailConfirmation === 'success') {
    toast.add({
      severity: 'success',
      summary: 'Email Confirmed',
      detail: 'Your account is now fully activated. You can now create API keys, connect runners, and publish applications.',
      life: 6000,
    })
  } else if (route.query.emailConfirmation === 'failed') {
    toast.add({
      severity: 'error',
      summary: 'Confirmation Failed',
      detail: 'That email confirmation link is no longer valid. Use the resend action to get a new one.',
      life: 6000,
    })
  } else {
    return
  }

  const nextQuery = { ...route.query }
  delete nextQuery.emailConfirmation
  void router.replace({ query: nextQuery })
}

// Load organization data on mount
onMounted(async () => {
  showEmailConfirmationFeedback()
  await loadOrganizations()
})
</script>

<template>
  <div class="dashboard-view">
    <!-- Pending Organization Invitations (shown for all users) -->
    <PendingInvitationsCard />

    <!-- Conditional Dashboard Components -->
    <AppUserDashboard v-if="isAppUserDashboard" />
    <StandardDashboard v-else />
  </div>
</template>

<style scoped>
.dashboard-view {
  max-width: 1400px;
  margin: 0 auto;
}
</style>
