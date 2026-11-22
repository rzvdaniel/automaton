<script setup lang="ts">
import Button from 'primevue/button'
import { requestLine, type PermissionRequest } from '../utils/permissionRequests'

defineProps<{ request: PermissionRequest; busy: boolean; showApp?: boolean; detail?: string }>()
defineEmits<{ decide: [approved: boolean] }>()
</script>

<template>
  <div class="request" data-testid="permission-request">
    <div class="request__text">
      <span v-if="showApp" class="request__line">{{ requestLine(request) }}</span>
      <code v-else class="request__line">{{ request.permission }}</code>
      <span v-if="detail" class="request__detail">{{ detail }}</span>
    </div>
    <div class="request__actions">
      <Button label="Approve" size="small" :aria-label="`Approve ${request.permission} for ${request.appName}`" :disabled="busy" @click="$emit('decide', true)" />
      <Button label="Reject" size="small" severity="secondary" outlined :aria-label="`Reject ${request.permission} for ${request.appName}`" :disabled="busy" @click="$emit('decide', false)" />
    </div>
  </div>
</template>

<style scoped>
.request { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px 12px; padding: 8px 0; }
.request__text { display: flex; flex-direction: column; gap: 2px; min-width: 0; flex: 1 1 16rem; }
.request__line { font-size: 14px; color: var(--ink); overflow-wrap: anywhere; }
code.request__line { font-family: var(--mono); font-size: 13px; }
.request__detail { font-size: 13px; color: var(--ink-soft); }
.request__actions { display: flex; gap: 8px; }
</style>
