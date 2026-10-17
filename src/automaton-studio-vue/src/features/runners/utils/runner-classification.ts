import { RunnerApplicationProfiles, type Runner } from '../types/runners.types'

export type RunnerClassification =
  | 'automaton-hosted'
  | 'self-hosted-standard'
  | 'self-hosted-machine-management'
  | 'self-hosted-privileged'
  | 'hidden-infrastructure'

export function classifyRunner(runner: Runner): RunnerClassification {
  if (runner.purpose?.toLowerCase() === 'infrastructure') return 'hidden-infrastructure'
  if (runner.isHostedRunner) return 'automaton-hosted'
  if (runner.applicationProfile === RunnerApplicationProfiles.PrivilegedHost) return 'self-hosted-privileged'
  if (runner.applicationProfile === RunnerApplicationProfiles.MachineManagement) return 'self-hosted-machine-management'
  return 'self-hosted-standard'
}

export function isVisibleApplicationRunner(runner: Runner): boolean {
  return classifyRunner(runner) !== 'hidden-infrastructure'
}
