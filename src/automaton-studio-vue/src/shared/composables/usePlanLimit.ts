/**
 * usePlanLimit composable
 * Provides global state for the plan limit upgrade dialog.
 * App.vue registers the callback; any component can trigger it via the axios interceptor.
 */
import { ref } from 'vue'
import { setPlanLimitCallback } from '@/shared/services/api/axios-instance'

const visible = ref(false)
const featureName = ref('')
const requiredPlan = ref('')

const featureLabels: Record<string, string> = {
  CanCreateProjects: 'Projects',
  CanCreateOrganizations: 'Organizations',
}

export function usePlanLimit() {
  function install() {
    setPlanLimitCallback((feature, plan) => {
      featureName.value = featureLabels[feature] ?? feature
      requiredPlan.value = plan
      visible.value = true
    })
  }

  function dismiss() {
    visible.value = false
  }

  /** Proactively show the upgrade dialog without waiting for a 402. */
  function showUpgrade(feature: string, plan: string) {
    featureName.value = featureLabels[feature] ?? feature
    requiredPlan.value = plan
    visible.value = true
  }

  return { visible, featureName, requiredPlan, install, dismiss, showUpgrade }
}
