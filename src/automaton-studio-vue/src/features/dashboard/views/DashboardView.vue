<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import StandardDashboard from '../components/StandardDashboard.vue'
import AppUserDashboard from '../components/AppUserDashboard.vue'
import PendingInvitationsCard from '@/features/organizations/components/PendingInvitationsCard.vue'

const { isAppUserOnly, loadOrganizations, organizations } = useOrganizationRole()

// Check if user is an AppUser with organizations
const hasOrganizations = computed(() => organizations.value.length > 0)
const isAppUserDashboard = computed(() => isAppUserOnly.value && hasOrganizations.value)

// Load organization data on mount
onMounted(async () => {
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
