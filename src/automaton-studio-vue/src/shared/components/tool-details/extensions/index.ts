import UiGetConsoleLogsDetails from './UiGetConsoleLogsDetails.vue'

export const toolDetailsExtensions = {
  ui_get_console_logs: UiGetConsoleLogsDetails,
} as const

export type ToolDetailsExtensionName = keyof typeof toolDetailsExtensions
