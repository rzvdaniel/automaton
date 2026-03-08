/** Returns true when subscription plans/upgrades are enabled for this environment. */
export function useSubscriptionsEnabled(): boolean {
  return import.meta.env.VITE_SUBSCRIPTIONS_ENABLED === 'true'
}
