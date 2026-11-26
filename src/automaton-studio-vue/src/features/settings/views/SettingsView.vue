<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PageHead from '@/shared/components/PageHead.vue'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { installService } from '@/features/install/services/install.service'
import SmtpSettingsForm from '@/features/install/components/SmtpSettingsForm.vue'
import AccountTab from '../components/AccountTab.vue'
import ConnectAiSection from '../components/ConnectAiSection.vue'
import ApiKeysSection from '../components/ApiKeysSection.vue'
import { settingsTabs, resolveSettingsTab, type SettingsTabKey } from '../settings-tabs'

const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()

const isSysAdmin = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const emailMissing = ref(false)

async function checkEmail() {
  if (!isSysAdmin.value) return
  try {
    // Older servers do not report the field; show no badge rather than guess.
    emailMissing.value = (await installService.getSummary()).emailConfigured === false
  } catch {
    emailMissing.value = false
  }
}
onMounted(checkEmail)
watch(isSysAdmin, checkEmail)

const tabs = computed(() => settingsTabs(isSysAdmin.value, emailMissing.value))
// A link to a tab this person does not have (Email for a non-administrator) shows Account.
const active = computed<SettingsTabKey>(() => resolveSettingsTab(route.hash, isSysAdmin.value))

function open(key: SettingsTabKey) {
  void router.replace({ hash: `#${key}` })
}
</script>

<template>
  <div class="settings-view studio-page">
    <PageHead title="Settings" :line="isSysAdmin ? 'Your account, your AI connections and keys, and this server’s email.' : 'Your account, your AI connections and keys.'" />

    <div class="tabs" role="tablist" aria-label="Settings sections">
      <button
        v-for="tab in tabs"
        :id="`settings-tab-${tab.key}`"
        :key="tab.key"
        type="button"
        role="tab"
        class="tab"
        :class="{ on: active === tab.key }"
        :aria-selected="active === tab.key"
        :aria-controls="`settings-panel-${tab.key}`"
        :data-testid="`settings-tab-${tab.key}`"
        @click="open(tab.key)"
      >{{ tab.label }}<span v-if="tab.badge" class="tab-badge" aria-label="not set up" data-testid="email-tab-badge">{{ tab.badge }}</span></button>
    </div>

    <div :id="`settings-panel-${active}`" class="panel" role="tabpanel" :aria-labelledby="`settings-tab-${active}`">
      <AccountTab v-if="active === 'account'" />
      <ConnectAiSection v-else-if="active === 'connect'" @open-keys="open('keys')" />
      <section v-else-if="active === 'keys'" class="studio-card"><ApiKeysSection /></section>
      <section v-else class="studio-card" data-testid="email-tab" aria-label="Email">
        <p v-if="emailMissing" class="studio-muted notset" data-testid="email-not-set-up">
          Email is not set up on this server, so password reset emails and invitations will not reach anyone.
        </p>
        <SmtpSettingsForm @saved="checkEmail" />
      </section>
    </div>
  </div>
</template>

<style scoped>
.settings-view { max-width: 900px; margin: 0 auto; }
.tabs { display: flex; gap: 4px; border-bottom: 1px solid var(--line); overflow-x: auto; margin-bottom: 16px; }
.tab {
  flex: none;
  min-height: 40px;
  padding: 0 14px;
  border: 0;
  border-bottom: 2px solid transparent;
  background: transparent;
  color: var(--ink-soft);
  font: inherit;
  white-space: nowrap;
  cursor: pointer;
}
.tab.on { border-bottom-color: var(--signal); color: var(--ink); font-weight: 600; }
.tab-badge { margin-left: 6px; min-width: 18px; padding: 0 6px; border-radius: 999px; background: var(--signal); color: var(--on-signal); font-size: 11px; font-weight: 600; line-height: 18px; display: inline-block; text-align: center; }
.notset { margin: 0; }
</style>
