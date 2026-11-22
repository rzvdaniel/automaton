import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useAppsStore } from '../stores/apps.store'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { App } from '../types/apps.types'

const isAdmin = () => (useAuthStore().user?.roles ?? []).some((role) => role === 'Admin' || role === 'SysAdmin')

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
          const message = error instanceof Error ? error.message : undefined
          toast.add({ severity: 'error', summary: 'The app could not be deleted', detail: message, life: 5000 })
          if (isAdmin() && message && /must be online|did not answer/i.test(message)) askDeleteAnyway(current)
        } finally {
          deleting.value = false
        }
      },
    })
  }

  /** Administrators only: the Runner is offline, so delete the app without cleaning up that machine. */
  function askDeleteAnyway(current: App) {
    confirm.require({
      header: 'Delete anyway?',
      message: `The Runner of "${current.name}" is offline. Delete anyway? The container and its data may stay on that machine. The deletion is recorded in the audit log (reason: Runner offline).`,
      icon: 'pi pi-exclamation-triangle',
      acceptClass: 'p-button-danger',
      acceptLabel: 'Delete anyway',
      accept: async () => {
        deleting.value = true
        try {
          await appsStore.deleteApp(current.id, true)
          toast.add({ severity: 'warn', summary: `"${current.name}" was deleted`, detail: 'Cleanup on its Runner was skipped.', life: 8000 })
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
