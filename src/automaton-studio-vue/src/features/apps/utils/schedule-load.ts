export function isCurrentScheduleLoad(
  requestedAppId: string,
  currentAppId: string,
  generation: number,
  currentGeneration: number,
): boolean {
  return requestedAppId === currentAppId && generation === currentGeneration
}
