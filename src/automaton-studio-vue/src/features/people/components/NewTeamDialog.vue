<script setup lang="ts">
import { ref, watch } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import { organizationsService } from '@/features/organizations/services/organizationsService'

const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ created: [] }>()

const name = ref('')
const saving = ref(false)
const error = ref<string | null>(null)

watch(visible, (open) => {
  if (!open) {
    name.value = ''
    error.value = null
  }
})

async function submit() {
  saving.value = true
  error.value = null
  try {
    await organizationsService.createOrganization({ name: name.value.trim() })
    visible.value = false
    emit('created')
  } catch (err) {
    const response = (err as { response?: { data?: { message?: string } } }).response
    error.value = response?.data?.message ?? 'The team could not be created.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="New team" :style="{ width: '26rem' }" :breakpoints="{ '640px': '95vw' }">
    <div class="form">
      <label class="field">
        <span>Team name</span>
        <InputText v-model="name" autocomplete="off" placeholder="Design" @keyup.enter="name.trim() && submit()" />
      </label>
      <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    </div>
    <template #footer>
      <Button label="Cancel" text @click="visible = false" />
      <Button label="Create team" :loading="saving" :disabled="!name.trim() || saving" @click="submit" />
    </template>
  </Dialog>
</template>

<style scoped>
.form { display: flex; flex-direction: column; gap: 0.9rem; }
.field { display: flex; flex-direction: column; gap: 0.35rem; }
.field :deep(input) { width: 100%; }
</style>
