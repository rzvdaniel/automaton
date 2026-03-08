/**
 * useBudgetBanner composable
 * Fetches the current user's monthly AI budget usage and exposes it as
 * reactive state. The banner component watches `showWarning` / `showDanger`.
 * Call `load()` once after login (alongside usePlanFeatures).
 */
import { ref, computed } from 'vue'
import api from '@/shared/services/api/axios-instance'

const currentMonthCostUsd = ref(0)
const monthlyCostLimitUsd = ref(0)
const percentUsed = ref(0)
const planName = ref<string | null>(null)

export function useBudgetBanner() {
  const showWarning = computed(() => percentUsed.value >= 80 && percentUsed.value < 100)
  const showDanger  = computed(() => percentUsed.value >= 100)
  const showBanner  = computed(() => showWarning.value || showDanger.value)

  async function load() {
    try {
      const { data } = await api.get<{
        currentMonthCostUsd: number
        monthlyCostLimitUsd: number
        percentUsed: number
        planName: string | null
      }>('/api/account/getbudgetusage')
      currentMonthCostUsd.value = data.currentMonthCostUsd
      monthlyCostLimitUsd.value = data.monthlyCostLimitUsd
      percentUsed.value = data.percentUsed
      planName.value = data.planName
    } catch {
      // silent — banner just won't show
    }
  }

  return {
    currentMonthCostUsd,
    monthlyCostLimitUsd,
    percentUsed,
    planName,
    showWarning,
    showDanger,
    showBanner,
    load,
  }
}
