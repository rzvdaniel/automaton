/**
 * Console types for step execution logging
 */

/**
 * Result from a step execution
 */
export interface StepExecutionResult {
  success: boolean
  status: string
  outputVariables?: Record<string, string>
  logs?: string[]
  output?: string
  error?: string
}

/**
 * A single entry in the console log
 */
export interface ConsoleEntry {
  id: string
  timestamp: Date
  type: 'step-execution' | 'runtime-log' | 'error' | 'info'
  stepName: string
  inputs: Record<string, unknown>
  result: StepExecutionResult
  duration?: number
  /** Where the execution originated from */
  source: 'application' | 'ai-test'
  level?: 'info' | 'warn' | 'error'
  stream?: 'stdout' | 'stderr'
  runnerName?: string
  deploymentTargetId?: string
}

export type RuntimeLogConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

/**
 * Message sent from application iframe to parent window
 */
export interface StepResultMessage {
  type: 'automatonStepResult'
  data: {
    stepName: string
    inputs: Record<string, unknown>
    result: StepExecutionResult
    duration: number
    timestamp: string
    source: 'application' | 'ai-test'
  }
}

export interface BackendRuntimeLogEvent {
  appId: string
  deploymentTargetId: string
  timestamp: string
  level: 'info' | 'warn' | 'error'
  message: string
  source: 'backend'
  stream: 'stdout' | 'stderr'
  controllerName?: string | null
  actionName?: string | null
  runnerName?: string | null
}
