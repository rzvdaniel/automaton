import { RunnerStatus, type Runner } from '@/features/runners/types/runners.types'
import { classifyRunner, isVisibleApplicationRunner } from '@/features/runners/utils/runner-classification'

export function getDeploymentTargetRunnerProjectId(projectId?: string | null): string | undefined {
  return projectId ?? undefined
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
    case 'machine-management': return 'Self-hosted · Machine Management'
    default: return 'Unsupported Runner profile'
  }
}
