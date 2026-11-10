<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import { useToast } from 'primevue/usetoast'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { adminUsersService } from '@/features/admin/services/adminUsersService'
import type { AdminUser } from '@/features/admin/types/admin.types'
import { organizationsService } from '@/features/organizations/services/organizationsService'
import type { OrganizationListItem } from '@/features/organizations/types'
import PageHead from '@/shared/components/PageHead.vue'
import AppFilterBar from '@/features/apps/components/AppFilterBar.vue'
import AdminAddUserDialog from '@/features/admin/components/AdminAddUserDialog.vue'
import InvitePeopleDialog from '../components/InvitePeopleDialog.vue'
import NewTeamDialog from '../components/NewTeamDialog.vue'
import { isPendingInvite, memberRows, type TeamWithMembers } from '../utils/people'

const router = useRouter()
const toast = useToast()
const auth = useAuthStore()

const isAdmin = computed(() => (auth.user?.roles ?? []).some((role) => role === 'Admin' || role === 'SysAdmin'))

const loading = ref(true)
const failed = ref(false)
const users = ref<AdminUser[] | null>(null)
const pending = ref<AdminUser[]>([])
const search = ref('')
const totalUsers = ref(0)
const teams = ref<OrganizationListItem[]>([])
const teamDetails = ref<TeamWithMembers[]>([])
const sharedApps = ref<Record<string, number>>({})
const showInvite = ref(false)
const showNewTeam = ref(false)
const showCreate = ref(false)
const isSysAdmin = computed(() => (auth.user?.roles ?? []).includes('SysAdmin'))
const resending = ref<string | null>(null)

const members = computed(() => memberRows(users.value, teamDetails.value))
const adminTeams = computed(() => teams.value.filter((team) => team.role === 'OrgAdmin'))
const canInvite = computed(() => isAdmin.value || adminTeams.value.length > 0)

async function load() {
  loading.value = true
  failed.value = false
  try {
    teams.value = await organizationsService.listOrganizations()
    // One team failing to load must not blank the page: it shows without members and with no app count.
    const [page, invited, details, apps] = await Promise.all([
      isAdmin.value ? adminUsersService.getUsers({ search: search.value.trim() || undefined, pageSize: 100 }) : Promise.resolve(null),
      isAdmin.value
        ? adminUsersService.getUsers({ registrationProvider: 'invitation', emailConfirmed: false, pageSize: 100 })
        : Promise.resolve(null),
      Promise.all(teams.value.map(async (team) => ({
        name: team.name,
        members: await organizationsService.getMembers(team.id).catch(() => []),
      }))),
      Promise.all(teams.value.map(async (team) => [
        team.id,
        await organizationsService.getOrganizationApps(team.id).then((list) => list.length).catch(() => undefined),
      ] as const)),
    ])
    users.value = page?.items.filter((user) => !isPendingInvite(user)) ?? null
    totalUsers.value = page?.totalCount ?? 0
    pending.value = invited?.items.filter(isPendingInvite) ?? []
    teamDetails.value = details
    sharedApps.value = Object.fromEntries(apps.filter((entry): entry is [string, number] => entry[1] !== undefined))
  } catch {
    failed.value = true
  } finally {
    loading.value = false
  }
}

async function resend(user: AdminUser) {
  resending.value = user.id
  try {
    await adminUsersService.inviteUser({ email: user.email, name: user.name || undefined })
    toast.add({ severity: 'success', summary: 'Invitation sent again', detail: user.email, life: 3000 })
  } catch (err) {
    const response = (err as { response?: { data?: { message?: string } } }).response
    toast.add({ severity: 'error', summary: 'Could not resend', detail: response?.data?.message ?? user.email, life: 4000 })
  } finally {
    resending.value = null
  }
}

function openPerson(id: string) {
  if (isAdmin.value) void router.push({ name: 'admin-user-detail', params: { userId: id } })
}

onMounted(load)
</script>

<template>
  <div class="people studio-page">
    <PageHead title="People" line="Who can use this server, and the teams they share apps in.">
      <Button label="New team" outlined class="btn-small" @click="showNewTeam = true" />
      <Button v-if="isAdmin" label="Create with password" outlined class="btn-small" @click="showCreate = true" />
      <Button v-if="canInvite" label="Invite people" class="btn-main" @click="showInvite = true" />
    </PageHead>

    <Message v-if="failed" severity="error" :closable="false">
      People could not be loaded.
      <Button label="Try again" link @click="load" />
    </Message>
    <div v-else-if="loading" class="studio-card" aria-busy="true"><Skeleton v-for="i in 3" :key="i" height="56px" border-radius="12px" /></div>

    <template v-else>
      <h2 id="members-title" class="studio-section">Members <span class="studio-section__count">{{ members.length }}</span></h2>
      <AppFilterBar
        v-if="isAdmin"
        v-model:search="search"
        label="Search people"
        placeholder="Search by name or email"
        :pills="[]"
        :active="() => false"
        @keyup.enter="load"
      />
      <div class="studio-card">
        <p v-if="!members.length" class="studio-muted">No one yet. Invite the first person.</p>
        <component
          :is="isAdmin ? 'button' : 'div'" v-for="person in members" :key="person.id" class="studio-row"
          :type="isAdmin ? 'button' : undefined" @click="openPerson(person.id)"
        >
          <span class="studio-row__text">
            <span class="studio-row__name">{{ person.name }}</span>
            <span class="studio-row__line">{{ person.name !== person.email ? person.email + ' · ' : '' }}{{ person.teams.join(', ') || 'No team' }}</span>
          </span>
          <span v-if="isAdmin" class="studio-muted">{{ person.appCount ?? 0 }} apps</span>
          <span class="studio-pill">{{ person.role }}</span>
        </component>
        <p v-if="isAdmin && totalUsers > 100" class="studio-muted">Showing the first 100 of {{ totalUsers }} accounts. Search to narrow the list.</p>
      </div>

      <template v-if="isAdmin && pending.length">
        <h2 id="pending-title" class="studio-section">Pending invites <span class="studio-section__count">{{ pending.length }}</span></h2>
        <div class="studio-card">
          <div v-for="user in pending" :key="user.id" class="studio-row">
            <span class="studio-row__text">
              <span class="studio-row__name">{{ user.email }}</span>
              <span class="studio-row__line">Invited {{ user.registeredAtUtc ? new Date(user.registeredAtUtc).toLocaleDateString() : '' }}</span>
            </span>
            <span class="studio-pill studio-pill--warn">Waiting to join</span>
            <Button label="Resend" outlined class="btn-small" :loading="resending === user.id" @click="resend(user)" />
          </div>
        </div>
      </template>

      <h2 id="teams-title" class="studio-section">Teams <span class="studio-section__count">{{ teams.length }}</span></h2>
      <div class="studio-card">
        <p v-if="!teams.length" class="studio-muted">No teams yet. A team shares apps with its members.</p>
        <button
          v-for="team in teams" :key="team.id" type="button" class="studio-row"
          @click="router.push({ name: 'organization-dashboard', params: { id: team.id } })"
        >
          <span class="studio-row__text">
            <span class="studio-row__name">{{ team.name }}</span>
            <span class="studio-row__line">{{ team.memberCount }} members · {{ sharedApps[team.id] ?? 0 }} shared apps</span>
          </span>
          <span class="studio-pill">{{ team.role === 'OrgAdmin' ? 'Team admin' : team.role === 'Developer' ? 'Developer' : 'App user' }}</span>
        </button>
      </div>
    </template>

    <InvitePeopleDialog v-model:visible="showInvite" :teams="adminTeams" :can-invite-accounts="isAdmin" @done="load" />
    <AdminAddUserDialog v-if="isAdmin" v-model:visible="showCreate" initial-mode="create" :can-grant-administrator="isSysAdmin" @added="load" />
    <NewTeamDialog v-model:visible="showNewTeam" @created="load" />
  </div>
</template>

<style scoped>
.people { max-width: 1400px; margin: 0 auto; }
</style>
