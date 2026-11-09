<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import Message from 'primevue/message'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { installService } from '../services/install.service'

const docsUrl = 'https://automaton.monster/docs/install-server#set-up-email'

const authStore = useAuthStore()
const route = useRoute()
// Only system administrators can change the server's email settings, so only they are told.
const isSystemAdministrator = computed(() => authStore.isAuthenticated && !!authStore.user?.roles?.includes('SysAdmin'))
const notConfigured = ref(false)

async function check() {
  try {
    // Older servers do not report the field; show nothing rather than guess.
    notConfigured.value = (await installService.getSummary()).emailConfigured === false
  } catch {
    notConfigured.value = false
  }
}

watch(isSystemAdministrator, async (allowed) => {
  notConfigured.value = false
  if (allowed) await check()
}, { immediate: true })

// While the notice shows, look again on each page change so it goes away after email is saved in Settings.
watch(() => route.fullPath, () => { if (notConfigured.value) void check() })
</script>

<template>
  <Message v-if="notConfigured" severity="warn" :closable="false" class="mb-4" data-testid="email-not-configured">
    Email is not set up on this server, so password reset emails and email invitations will not reach anyone.
    <RouterLink to="/settings#server">Set it up in Settings</RouterLink>
    or read <a :href="docsUrl" target="_blank" rel="noopener">how to set up email</a>.
  </Message>
</template>
