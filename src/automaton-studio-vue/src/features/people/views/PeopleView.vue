<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { adminUsersService } from '@/features/admin/services/adminUsersService'
import type { AdminUser } from '@/features/admin/types/admin.types'
import { organizationsService } from '@/features/organizations/services/organizationsService'
import type { OrganizationListItem } from '@/features/organizations/types'
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
  <div class="people">
    <header class="head">
      <div>
        <h1>People</h1>
        <p class="sub">Who can use this server, and the teams they share apps in.</p>
      </div>
      <div class="actions">
        <Button label="New team" icon="pi pi-plus" severity="secondary" outlined @click="showNewTeam = true" />
        <Button v-if="isAdmin" label="Create with password" icon="pi pi-key" severity="secondary" text @click="showCreate = true" />
        <Button v-if="canInvite" label="Invite people" icon="pi pi-user-plus" @click="showInvite = true" />
      </div>
    </header>

    <Message v-if="failed" severity="error" :closable="false">
      People could not be loaded.
      <Button label="Try again" link @click="load" />
    </Message>
    <Skeleton v-else-if="loading" height="12rem" />

    <template v-else>
      <section aria-labelledby="members-title">
        <h2 id="members-title">Members <span class="count">{{ members.length }}</span></h2>
        <input v-if="isAdmin" v-model="search" class="search" type="search" placeholder="Search by name or email" aria-label="Search people" @keyup.enter="load" />
        <p v-if="!members.length" class="empty">No one yet. Invite the first person.</p>
        <table v-else class="rows">
          <thead>
            <tr><th>Name</th><th>Role</th><th>Teams</th><th v-if="isAdmin" class="num">Apps</th></tr>
          </thead>
          <tbody>
            <tr v-for="person in members" :key="person.id" :class="{ clickable: isAdmin }" @click="openPerson(person.id)">
              <td>
                <div class="name">{{ person.name }}</div>
                <div v-if="person.name !== person.email" class="muted">{{ person.email }}</div>
              </td>
              <td>{{ person.role }}</td>
              <td>{{ person.teams.join(', ') || '—' }}</td>
              <td v-if="isAdmin" class="num">{{ person.appCount ?? '—' }}</td>
            </tr>
          </tbody>
        </table>
        <p v-if="isAdmin && totalUsers > 100" class="muted">Showing the first 100 of {{ totalUsers }} accounts. Search to narrow the list.</p>
      </section>

      <section v-if="isAdmin" aria-labelledby="pending-title">
        <h2 id="pending-title">Pending invites <span class="count">{{ pending.length }}</span></h2>
        <p v-if="!pending.length" class="empty">No pending invites.</p>
        <table v-else class="rows">
          <thead>
            <tr><th>Email</th><th>Invited</th><th></th></tr>
          </thead>
          <tbody>
            <tr v-for="user in pending" :key="user.id">
              <td>
                <div class="name">{{ user.email }}</div>
                <Tag value="Waiting to join" severity="warn" />
              </td>
              <td>{{ user.registeredAtUtc ? new Date(user.registeredAtUtc).toLocaleDateString() : '—' }}</td>
              <td class="num">
                <Button label="Resend" icon="pi pi-send" size="small" text :loading="resending === user.id" @click="resend(user)" />
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <section aria-labelledby="teams-title">
        <h2 id="teams-title">Teams <span class="count">{{ teams.length }}</span></h2>
        <p v-if="!teams.length" class="empty">No teams yet. A team shares apps with its members.</p>
        <table v-else class="rows">
          <thead>
            <tr><th>Team</th><th class="num">Members</th><th class="num">Shared apps</th></tr>
          </thead>
          <tbody>
            <tr v-for="team in teams" :key="team.id" class="clickable" @click="router.push({ name: 'organization-dashboard', params: { id: team.id } })">
              <td>
                <div class="name">{{ team.name }}</div>
                <div class="muted">You are {{ team.role === 'OrgAdmin' ? 'a team admin' : team.role === 'Developer' ? 'a developer' : 'an app user' }}</div>
              </td>
              <td class="num">{{ team.memberCount }}</td>
              <td class="num">{{ sharedApps[team.id] ?? '—' }}</td>
            </tr>
          </tbody>
        </table>
      </section>
    </template>

    <InvitePeopleDialog v-model:visible="showInvite" :teams="adminTeams" :can-invite-accounts="isAdmin" @done="load" />
    <AdminAddUserDialog v-if="isAdmin" v-model:visible="showCreate" :can-grant-administrator="isSysAdmin" @added="load" />
    <NewTeamDialog v-model:visible="showNewTeam" @created="load" />
  </div>
</template>

<style scoped>
.people { padding: 1.5rem; max-width: 64rem; display: flex; flex-direction: column; gap: 1.75rem; }
.head { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; flex-wrap: wrap; }
h1 { margin: 0; font-size: 1.75rem; font-weight: 600; }
h2 { margin: 0 0 0.6rem; font-size: 1.1rem; font-weight: 600; }
.sub { margin: 0.25rem 0 0; color: var(--p-text-muted-color); }
.actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
.count { margin-left: 0.4rem; font-weight: 400; color: var(--p-text-muted-color); }
.empty, .muted { color: var(--p-text-muted-color); font-size: 0.9rem; margin: 0; }
.rows { width: 100%; border-collapse: collapse; }
.rows th { text-align: left; font-weight: 500; font-size: 0.85rem; color: var(--p-text-muted-color); padding: 0.5rem 0.75rem; border-bottom: 1px solid var(--p-content-border-color); }
.rows td { padding: 0.65rem 0.75rem; border-bottom: 1px solid var(--p-content-border-color); vertical-align: middle; }
.rows .num { text-align: right; white-space: nowrap; }
.name { font-weight: 500; }
.search { width: 100%; max-width: 20rem; margin-bottom: 0.6rem; padding: 0.45rem 0.6rem; border: 1px solid var(--p-content-border-color); border-radius: 6px; background: var(--p-content-background); color: inherit; }
.clickable { cursor: pointer; }
.clickable:hover { background: var(--p-content-hover-background); }
@media (max-width: 640px) { .people { padding: 1rem; } }
</style>
