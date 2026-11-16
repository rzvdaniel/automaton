import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useAppsStore } from '../stores/apps.store'
import type { App } from '../types/apps.types'

/** Asks, then deletes the app and goes back to the list; shared by the header menu and the Danger zone. */
export function useDeleteApp(app: () => App) {
  const router = useRouter()
  const toast = useToast()
  const confirm = useConfirm()
  const appsStore = useAppsStore()
  const deleting = ref(false)

  function askDelete() {
    const current = app()
    confirm.require({
      header: 'Delete this app?',
      message: `Delete "${current.name}" and all of its data? This cannot be undone.`,
      icon: 'pi pi-exclamation-triangle',
      acceptClass: 'p-button-danger',
      acceptLabel: 'Delete',
      accept: async () => {
        deleting.value = true
        try {
          await appsStore.deleteApp(current.id)
          toast.add({ severity: 'success', summary: `"${current.name}" was deleted`, life: 3000 })
          await router.push('/apps')
        } catch (error) {
          toast.add({ severity: 'error', summary: 'The app could not be deleted', detail: error instanceof Error ? error.message : undefined, life: 5000 })
        } finally {
          deleting.value = false
        }
      },
    })
  }

  return { deleting, askDelete }
}
