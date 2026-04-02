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
  source: 'preview' | 'ai-test'
}

/**
 * Message sent from preview iframe to parent window
 */
export interface StepResultMessage {
  type: 'automatonStepResult'
  data: {
    stepName: string
    inputs: Record<string, unknown>
    result: StepExecutionResult
    duration: number
    timestamp: string
    source: 'preview' | 'ai-test'
  }
}

export const UiLogLevel = {
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
  source: 'preview-console' | 'preview-runtime' | 'preview-network'
}

export interface PreviewConsoleMessage {
  type: 'previewConsoleEvent'
  data: {
    level: UiLogLevel
    message: string
    timestamp: string
    source: 'preview-console' | 'preview-runtime' | 'preview-network'
  }
}
