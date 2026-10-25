<script setup lang="ts">
import { ref, watch } from 'vue'
import axios from 'axios'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import { installService } from '../services/install.service'
import type { InstallationCheck } from '../types/install.types'

const props = withDefaults(defineProps<{ setupToken?: string }>(), { setupToken: '' })
const emit = defineEmits<{ ready: [ready: boolean] }>()
const checks = ref<InstallationCheck[]>([])
const loading = ref(false)
const error = ref('')
watch(() => props.setupToken, () => { checks.value = []; emit('ready', false) })
async function refresh() {
  loading.value = true
  error.value = ''
  checks.value = []
  emit('ready', false)
  const checkedToken = props.setupToken
  try {
    const result = await installService.check(checkedToken)
    if (checkedToken !== props.setupToken) return
    checks.value = result
    emit('ready', result.length > 0 && result.every(check => check.status !== 'error'))
  } catch (cause) {
    error.value = axios.isAxiosError(cause) && cause.response?.status === 403
      ? 'Enter a valid setup token, or sign in as the system administrator.'
      : 'Unable to check this installation. Check the API connection and retry.'
  } finally { loading.value = false }
}
</script>
<template>
  <div class="flex flex-column gap-3">
    <Button type="button" label="Check installation" icon="pi pi-refresh" outlined :loading="loading" @click="refresh" />
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    <div v-for="check in checks" :key="check.name" class="flex align-items-start justify-content-between gap-3">
      <div><strong>{{ check.name }}</strong><p class="text-color-secondary mt-1 mb-0">{{ check.detail }}</p></div>
      <Tag :value="check.status" :severity="check.status === 'error' ? 'danger' : check.status === 'verified' ? 'success' : 'secondary'" />
    </div>
  </div>
</template>
