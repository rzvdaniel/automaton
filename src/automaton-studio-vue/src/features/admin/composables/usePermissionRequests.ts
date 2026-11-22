import { computed, ref } from 'vue'
import { adminAppsService } from '../services/adminAppsService'
import { toRequests, type PermissionRequest } from '../utils/permissionRequests'

/** The permission requests waiting for a system administrator, with approve and reject. */
export function usePermissionRequests() {
  const requests = ref<PermissionRequest[]>([])
  const loading = ref(false)
  const failed = ref(false)
  const actionFailed = ref(false)
  const busy = ref(false)

  async function load() {
    loading.value = true
    failed.value = false
    try {
      const result = await adminAppsService.listApps({ waiting: true, pageSize: 100 })
      requests.value = toRequests(result.items)
    } catch {
      failed.value = true
    } finally {
      loading.value = false
    }
  }

  async function decide(items: PermissionRequest[], approved: boolean) {
    busy.value = true
    actionFailed.value = false
    try {
      for (const item of items) await adminAppsService.setPermission(item.appId, item.permission, approved)
    } catch {
      actionFailed.value = true
    } finally {
      await load()
      busy.value = false
    }
  }

  return { requests, loading, failed, actionFailed, busy, load, decide, count: computed(() => requests.value.length) }
}
