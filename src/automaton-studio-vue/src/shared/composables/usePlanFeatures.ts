/**
 * usePlanFeatures composable
 * Loads and caches the current user's plan feature flags (singleton per session).
 * Call `load()` once after login (e.g. in App.vue). Use `canCreateProjects`
 * and `canCreateOrganizations` anywhere in the app.
 */
import { ref } from 'vue'
import api from '@/shared/services/api/axios-instance'

const canCreateProjects = ref(true)
const canCreateOrganizations = ref(true)
const loaded = ref(false)

export function usePlanFeatures() {
  async function load() {
    try {
      const { data } = await api.get<{
        canCreateProjects: boolean
        canCreateOrganizations: boolean
      }>('/api/account/getplanfeatures')
      canCreateProjects.value = data.canCreateProjects
      canCreateOrganizations.value = data.canCreateOrganizations
    } catch {
      // If the request fails (e.g. not yet logged in), keep defaults (allow all)
    } finally {
      loaded.value = true
    }
  }

  function reset() {
    canCreateProjects.value = true
    canCreateOrganizations.value = true
    loaded.value = false
  }

  return { canCreateProjects, canCreateOrganizations, loaded, load, reset }
}
