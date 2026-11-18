import type { ToastServiceMethods } from 'primevue/toastservice'
import { appsService, type CatalogUpdate } from '../services/apps.service'
import { appActionErrorMessage } from './app-action-error'

/** One short line of a deploy error: its first line and, when it has more, the last (a traceback ends with the cause). Same rule as Home. */
export function shortReason(error: string | null | undefined): string {
  const lines = (error ?? '').split('\n').map((line) => line.trim()).filter(Boolean)
  if (lines.length === 0) return ''
  const text = lines.length === 1 ? lines[0]! : `${lines[0]!.replace(/:+$/, '')}: ${lines[lines.length - 1]!}`
  return text.length <= 200 ? text : `${text.slice(0, 199)}…`
}

/** The last deploy failed on the commit the app has, so the button retries instead of updating. */
export const isRetryOnly = (update: Pick<CatalogUpdate, 'deployFailed' | 'installedCommit' | 'latestCommit'>) =>
  !!update.deployFailed && update.installedCommit === update.latestCommit

/** Replaces the app with the catalog's newer commit and redeploys it; asks first when the owner edited the files. Returns whether it started. */
export async function applyCatalogUpdate(appId: string, update: CatalogUpdate, toast: ToastServiceMethods): Promise<boolean> {
  if (update.edited && !window.confirm('You edited this app after installing it. Updating replaces your changes with the new version. Update anyway?')) return false
  try {
    await appsService.applyCatalogUpdate(appId, update.edited)
    toast.add({ severity: 'success', summary: 'Updating', detail: 'The new version is being deployed.', life: 5000 })
    return true
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Update failed', detail: appActionErrorMessage(error, 'The app could not be updated.'), life: 8000 })
    return false
  }
}
