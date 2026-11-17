<script setup lang="ts">
import { computed, ref } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { useHomeStore } from '../stores/home.store'

/** Pins an app to the signed-in person's Home, or removes it from there. */
const props = defineProps<{ appId: string; appName: string; compact?: boolean }>()

const store = useHomeStore()
const toast = useToast()
const busy = ref(false)
const pinned = computed(() => store.pinnedIds.has(props.appId))

async function toggle() {
  busy.value = true
  try {
    if (pinned.value) await store.unpin(props.appId)
    else await store.pin(props.appId)
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: `"${props.appName}" could not be ${pinned.value ? 'unpinned' : 'pinned'}.`, life: 5000 })
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <Button
    :label="compact ? undefined : pinned ? 'Pinned' : 'Pin to Home'"
    :icon="pinned ? 'pi pi-bookmark-fill' : 'pi pi-bookmark'"
    size="small"
    severity="secondary"
    outlined
    :loading="busy"
    :aria-pressed="pinned"
    :aria-label="`${pinned ? 'Unpin' : 'Pin'} ${appName} ${pinned ? 'from' : 'to'} Home`"
    data-testid="pin-to-home"
    @click.stop.prevent="toggle"
  />
</template>
