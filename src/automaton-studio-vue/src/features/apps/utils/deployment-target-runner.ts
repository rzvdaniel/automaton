import { RunnerApplicationProfiles, RunnerStatus, type Runner } from '@/features/runners/types/runners.types'
import { classifyRunner, isVisibleApplicationRunner } from '@/features/runners/utils/runner-classification'

export function getDeploymentTargetRunnerProjectId(projectId?: string | null): string | undefined {
  return projectId ?? undefined
}

export function isPrivilegedSelfHostedRunner(runner: Runner | undefined): boolean {
  return runner?.applicationProfile === RunnerApplicationProfiles.PrivilegedHost && !runner.isHostedRunner
}

export function requiresPrivilegedHostAcknowledgement(
  runner: Runner | undefined,
  selectedRunnerId: string | null,
  currentRunnerId: string | null,
): boolean {
  return isPrivilegedSelfHostedRunner(runner) && selectedRunnerId !== currentRunnerId
}

export function shouldResetPrivilegedHostAcknowledgement(
  nextRunnerId: string | null,
  previousRunnerId: string | null,
): boolean {
  return nextRunnerId !== previousRunnerId
}

export function getDeploymentTargetRunnerOptions(runners: Runner[]) {
  return runners
    .filter(isVisibleApplicationRunner)
    .map((runner) => ({
      label: `${runner.name} · ${runnerSourceLabel(runner)} · ${runner.status}`,
      value: runner.id,
      disabled: runner.status !== RunnerStatus.Online,
    }))
}

export function runnerSourceLabel(runner: Runner): string {
  switch (classifyRunner(runner)) {
    case 'automaton-hosted': return 'Automaton-hosted'
    case 'self-hosted-privileged': return 'Self-hosted · Privileged Host'
    default: return 'Self-hosted · Standard'
  }
}
