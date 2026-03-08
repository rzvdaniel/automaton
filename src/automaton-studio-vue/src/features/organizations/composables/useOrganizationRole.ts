/**
 * useOrganizationRole composable
 * Provides role-based access control helpers for organizations
 */
import { ref, computed } from 'vue'
import { organizationsService } from '../services/organizationsService'
import { OrganizationRole } from '../types'
import type { OrganizationListItem } from '../types'

const organizations = ref<OrganizationListItem[]>([])
const loading = ref(false)

export function useOrganizationRole() {

  /**
   * Check if user has only AppUser role across all organizations
   * (no OrgAdmin or Developer roles anywhere)
   */
  const isAppUserOnly = computed(() => {
    if (loading.value) return false
    if (organizations.value.length === 0) return false
    
    return organizations.value.every(org => 
      org.role === OrganizationRole.AppUser
    )
  })

  /**
   * Check if user has at least one Developer or OrgAdmin role
   */
  const canCreateContent = computed(() => {
    return organizations.value.some(org => 
      org.role === OrganizationRole.OrgAdmin || 
      org.role === OrganizationRole.Developer
    )
  })

  /**
   * Check if user has at least one OrgAdmin role
   */
  const hasOrgAdminRole = computed(() => {
    return organizations.value.some(org => 
      org.role === OrganizationRole.OrgAdmin
    )
  })

  /**
   * Load user's organizations to determine roles
   */
  async function loadOrganizations() {
    try {
      loading.value = true
      organizations.value = await organizationsService.listOrganizations()
    } catch (error) {
      // Error already handled by service
      organizations.value = []
    } finally {
      loading.value = false
    }
  }

  /**
   * Get highest role across all organizations
   * OrgAdmin > Developer > AppUser
   */
  const highestRole = computed(() => {
    if (organizations.value.length === 0) return null
    
    if (hasOrgAdminRole.value) return OrganizationRole.OrgAdmin
    if (canCreateContent.value) return OrganizationRole.Developer
    return OrganizationRole.AppUser
  })

  return {
    organizations,
    loading,
    isAppUserOnly,
    canCreateContent,
    hasOrgAdminRole,
    highestRole,
    loadOrganizations,
  }
}
