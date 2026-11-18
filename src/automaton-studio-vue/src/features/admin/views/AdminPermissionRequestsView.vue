<script setup lang="ts">
import { computed, nextTick, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Message from 'primevue/message'
import PermissionRequestRow from '../components/PermissionRequestRow.vue'
import { usePermissionRequests } from '../composables/usePermissionRequests'
import { allowsText, groupByApp } from '../utils/permissionRequests'

const route = useRoute()
const { requests, loading, failed, actionFailed, busy, load, decide } = usePermissionRequests()
const groups = computed(() => groupByApp(requests.value))

onMounted(async () => {
  await load()
  // Review links from Admin > Apps land on that app's section.
  const appId = route.query.app
  if (typeof appId === 'string') {
    await nextTick()
    document.getElementById(`requests-${appId}`)?.scrollIntoView({ block: 'start' })
  }
})
</script>

<template>
  <div class="requests-view">
    <div>
      <h2 class="page-title">Permission requests</h2>
      <p class="page-subtitle">What apps ask this server for. Nothing is granted until you approve it.</p>
    </div>

    <Message v-if="failed" severity="error" :closable="false">
      <div class="retry"><span>The requests could not be loaded.</span><Button label="Retry" size="small" severity="danger" outlined @click="load()" /></div>
    </Message>
    <Message v-if="actionFailed" severity="error" :closable="false">The change could not be saved.</Message>

    <Card v-if="!failed && !loading && groups.length === 0">
      <template #content><p class="empty" data-testid="requests-empty">No app is waiting for you.</p></template>
    </Card>

    <Card v-for="group in groups" :id="`requests-${group.appId}`" :key="group.appId" data-testid="request-group">
      <template #title>
        <div class="group-head">
          <RouterLink :to="`/apps/${group.appId}?view=admin`" class="group-name">{{ group.appName }}</RouterLink>
          <Button
            v-if="group.items.length > 1"
            :label="`Approve all (${group.items.length})`"
            size="small"
            :aria-label="`Approve all ${group.items.length} requests of ${group.appName}`"
            :disabled="busy"
            @click="decide(group.items, true)"
          />
        </div>
      </template>
      <template #content>
        <PermissionRequestRow
          v-for="request in group.items"
          :key="request.permission"
          :request="request"
          :busy="busy"
          show-app
          :detail="allowsText(request.permission)"
          @decide="(approved) => decide([request], approved)"
        />
      </template>
    </Card>
  </div>
</template>

<style scoped>
.requests-view { display: flex; flex-direction: column; gap: 1rem; }
.page-title { margin: 0; font-size: 1.75rem; font-weight: 700; }
.page-subtitle { margin: 0.35rem 0 0; color: var(--p-text-muted-color, var(--p-text-color-secondary)); }
.empty { margin: 0; text-align: center; padding: 1rem; color: var(--p-text-muted-color, var(--p-text-color-secondary)); }
.group-head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; font-size: 1.1rem; }
.group-name { color: var(--p-primary-color); font-weight: 600; text-decoration: none; }
.group-name:hover { text-decoration: underline; }
.retry { display: flex; align-items: center; justify-content: space-between; gap: 8px; width: 100%; }
</style>
