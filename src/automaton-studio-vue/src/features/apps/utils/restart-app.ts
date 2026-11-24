import type { ToastServiceMethods } from 'primevue/toastservice'
import { appsService } from '../services/apps.service'
import { appActionErrorMessage } from './app-action-error'

/** Restarts the running app so it loads its current settings and secrets, and tells the person how it went. */
export async function restartApp(appId: string, toast: ToastServiceMethods): Promise<void> {
  try {
    const { restarted, confirmed } = await appsService.restart(appId)
    toast.add({
      severity: restarted > 0 && confirmed ? 'success' : 'info',
      summary: restarted > 0 ? 'Restarting' : 'Nothing to restart',
      detail: restarted > 0
        ? (confirmed ? 'The app restarts with its current settings and secrets.' : 'The connection dropped before the answer. The server carries on with the restart; reload in a minute to check.')
        : 'The app is not running. Settings and secrets apply the next time it starts.',
      life: 4000,
    })
  } catch (failure) {
    toast.add({ severity: 'error', summary: 'Could not restart', detail: appActionErrorMessage(failure, 'The app could not be restarted.'), life: 12000 })
  }
}
