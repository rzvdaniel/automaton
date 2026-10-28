import { RunnerApplicationProfiles, type Runner } from '../types/runners.types'

export type RunnerClassification =
  | 'machine-management'
  | 'unsupported'

export function classifyRunner(runner: Runner): RunnerClassification {
  if (!runner.isHostedRunner && runner.applicationProfile === RunnerApplicationProfiles.MachineManagement) return 'machine-management'
  return 'unsupported'
}

export function isVisibleApplicationRunner(runner: Runner): boolean {
  return classifyRunner(runner) === 'machine-management'
}
