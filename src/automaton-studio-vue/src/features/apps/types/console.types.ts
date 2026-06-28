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
  error?: string
}

/**
 * A single entry in the console log
 */
export interface ConsoleEntry {
  id: string
  timestamp: Date
  type: 'step-execution' | 'error' | 'info'
  stepName: string
  inputs: Record<string, unknown>
  result: StepExecutionResult
  duration?: number
  /** Where the execution originated from */
  source: 'application' | 'ai-test'
}

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

export const UiLogLevel = {
  Debug: 'debug',
  Info: 'info',
  Warn: 'warn',
  Error: 'error',
} as const

export type UiLogLevel = typeof UiLogLevel[keyof typeof UiLogLevel]

export interface UiLogEntry {
  id: string
  timestamp: Date
  level: UiLogLevel
  message: string
  source: 'application-console' | 'application-runtime' | 'application-network'
}

export interface ApplicationConsoleMessage {
  type: 'applicationConsoleEvent'
  data: {
    level: UiLogLevel
    message: string
    timestamp: string
    source: 'application-console' | 'application-runtime' | 'application-network'
  }
}
