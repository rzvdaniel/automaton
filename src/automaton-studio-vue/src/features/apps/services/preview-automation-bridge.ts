import type { UiLogEntry } from '../types/console.types'

export const UiAutomationAction = {
  Navigate: 'ui_navigate',
  Reload: 'ui_reload',
  Click: 'ui_click',
  Type: 'ui_type',
  ReadText: 'ui_read_text',
  WaitFor: 'ui_wait_for',
  Assert: 'ui_assert',
  GetPageSnapshot: 'ui_get_page_snapshot',
  GetConsoleLogs: 'ui_get_console_logs',
  SetBackendMode: 'ui_set_backend_mode',
  Evaluate: 'ui_evaluate',
  Keyboard: 'ui_keyboard',
  GetAttribute: 'ui_get_attribute',
  QueryAll: 'ui_query_all',
  Select: 'ui_select',
  Hover: 'ui_hover',
  ScrollTo: 'ui_scroll_to',
  Check: 'ui_check',
  GetCanvasData: 'ui_get_canvas_data',
  NetworkLog: 'ui_network_log',
} as const

export type UiAutomationAction = typeof UiAutomationAction[keyof typeof UiAutomationAction]

export interface UiAutomationResult {
  success: boolean
  action: UiAutomationAction
  details?: string
  value?: string
  logs?: Array<Pick<UiLogEntry, 'timestamp' | 'level' | 'source' | 'message'>>
  logSummary?: {
    totalLogs: number
    returnedLogs: number
    droppedLogs: number
    levelCounts: {
      debug: number
      info: number
      warn: number
      error: number
    }
    hasBlockingIssues: boolean
    blockingIssueCount: number
    blockingIssues: Array<Pick<UiLogEntry, 'timestamp' | 'level' | 'source' | 'message'>>
  }
  snapshot?: {
    title: string
    url: string
    bodyText: string
  }
  // ui_get_attribute
  attributes?: Record<string, string | null>
  // ui_query_all
  count?: number
  items?: string[]
  // ui_get_canvas_data
  width?: number
  height?: number
  hasNonBlackPixels?: boolean
  sampleAlpha?: number
  // ui_network_log
  entries?: Array<{
    type: string
    url: string
    method: string
    status: number | null
    ok: boolean
    duration: number
    timestamp: string
    error?: string
  }>
  // ui_set_backend_mode
  mode?: string
  error?: string
}

const PREVIEW_AUTOMATION_TIMEOUT_MS = 12000

const UI_LOG_LEVEL_RANK: Record<UiLogEntry['level'], number> = {
  debug: 10,
  info: 20,
  warn: 30,
  error: 40,
}

function normalizeUiLogLevel(value: unknown): UiLogEntry['level'] {
  if (typeof value !== 'string') {
    return 'warn'
  }

  const normalized = value.toLowerCase()
  if (normalized === 'debug' || normalized === 'info' || normalized === 'warn' || normalized === 'error') {
    return normalized
  }

  return 'warn'
}

function buildUiLogSummary(logs: UiLogEntry[], returnedLogs: UiAutomationResult['logs']): UiAutomationResult['logSummary'] {
  const levelCounts = {
    debug: 0,
    info: 0,
    warn: 0,
    error: 0,
  }

  for (const entry of logs) {
    levelCounts[entry.level] += 1
  }

  const blockingIssues = logs
    .filter((entry) => entry.level === 'error')
    .slice(0, 20)
    .map((entry) => ({
      timestamp: entry.timestamp,
      level: entry.level,
      source: entry.source,
      message: entry.message,
    }))

  const returnedCount = returnedLogs?.length ?? 0
  const droppedLogs = Math.max(0, logs.length - returnedCount)

  return {
    totalLogs: logs.length,
    returnedLogs: returnedCount,
    droppedLogs,
    levelCounts,
    hasBlockingIssues: blockingIssues.length > 0,
    blockingIssueCount: blockingIssues.length,
    blockingIssues,
  }
}

function truncateLogMessage(message: string, maxChars: number): string {
  const normalized = message.trim().replace(/\s+/g, ' ')
  if (normalized.length <= maxChars) {
    return normalized
  }

  return `${normalized.slice(0, Math.max(0, maxChars - 1))}…`
}

function getPreviewIframe(): HTMLIFrameElement | null {
  return (
    document.querySelector<HTMLIFrameElement>('.build-visual-workspace .preview-iframe')
    ?? document.querySelector<HTMLIFrameElement>('.preview-overlay .preview-iframe')
  )
}

async function executeViaPostMessageBridge(
  action: UiAutomationAction,
  args: Record<string, unknown>,
): Promise<UiAutomationResult> {
  const iframe = getPreviewIframe()
  const targetWindow = iframe?.contentWindow
  if (!targetWindow) {
    return { success: false, action, error: 'Preview iframe is not available.' }
  }

  const requestId = crypto.randomUUID()
  const timeoutMs = Math.max(
    PREVIEW_AUTOMATION_TIMEOUT_MS,
    Number(args.timeoutMs ?? 0) + 2000,
  )

  return new Promise<UiAutomationResult>((resolve) => {
    const timeoutHandle = window.setTimeout(() => {
      window.removeEventListener('message', onMessage)
      resolve({
        success: false,
        action,
        error: `Timed out waiting for preview automation response (${timeoutMs}ms).`,
      })
    }, timeoutMs)

    function onMessage(event: MessageEvent) {
      const data = event.data as {
        type?: string
        requestId?: string
        result?: UiAutomationResult
      }

      if (data?.type !== 'previewAutomationResponse' || data.requestId !== requestId) {
        return
      }

      window.clearTimeout(timeoutHandle)
      window.removeEventListener('message', onMessage)
      resolve(
        data.result ?? {
          success: false,
          action,
          error: 'Preview automation returned an empty response.',
        },
      )
    }

    window.addEventListener('message', onMessage)

    targetWindow.postMessage(
      {
        type: 'previewAutomationRequest',
        requestId,
        action,
        args,
      },
      '*',
    )
  })
}

export async function executeUiAutomationAction(
  action: UiAutomationAction,
  args: Record<string, unknown>,
  getUiLogs: () => UiLogEntry[],
): Promise<UiAutomationResult> {
  // GetConsoleLogs reads parent-frame store data — never goes through the bridge.
  if (action === UiAutomationAction.GetConsoleLogs) {
    const requestedLimit = Number(args.limit ?? 120)
    const limit = Math.min(300, Math.max(20, Number.isFinite(requestedLimit) ? requestedLimit : 120))
    const minLevel = normalizeUiLogLevel(args.minLevel)
    const maxMessageCharsRequested = Number(args.maxMessageChars ?? 260)
    const maxMessageChars = Math.min(600, Math.max(80, Number.isFinite(maxMessageCharsRequested) ? maxMessageCharsRequested : 260))

    const allLogs = getUiLogs()
    const filteredLogs = allLogs.filter((entry) => UI_LOG_LEVEL_RANK[entry.level] >= UI_LOG_LEVEL_RANK[minLevel])

    const logs = filteredLogs.slice(0, limit).map((entry) => ({
      timestamp: entry.timestamp,
      level: entry.level,
      source: entry.source,
      message: truncateLogMessage(entry.message, maxMessageChars),
    }))

    const summary = buildUiLogSummary(filteredLogs, logs)
    const hasBlockingIssues = summary?.hasBlockingIssues ?? false
    const blockingIssueCount = summary?.blockingIssueCount ?? 0
    const details = hasBlockingIssues
      ? `Collected ${logs.length}/${filteredLogs.length} ${minLevel}+ logs. Blocking errors detected: ${blockingIssueCount}.`
      : `Collected ${logs.length}/${filteredLogs.length} ${minLevel}+ logs. No blocking errors detected.`

    return { success: true, action, logs, logSummary: summary, details }
  }

  // Reload needs to listen to the iframe load event from the parent side —
  // it cannot be detected from within the iframe itself.
  if (action === UiAutomationAction.Reload) {
    const waitForSel = args.waitForSelector !== undefined ? String(args.waitForSelector).trim() : ''
    const timeoutMs = Number(args.timeoutMs ?? 5000)

    const iframeEl = getPreviewIframe()
    if (!iframeEl) {
      return { success: false, action, error: 'Preview iframe is not available' }
    }

    // Register load listener BEFORE triggering reload to avoid a race condition.
    const loadPromise = new Promise<boolean>((resolve) => {
      const timeoutHandle = window.setTimeout(() => resolve(false), timeoutMs)
      iframeEl.addEventListener('load', () => { window.clearTimeout(timeoutHandle); resolve(true) }, { once: true })
    })

    // Ask the iframe to reload via the bridge (works for both same-origin and cross-origin).
    void executeViaPostMessageBridge(action, args)

    const loaded = await loadPromise
    if (!loaded) {
      return { success: false, action, error: `Timed out waiting for page to reload (${timeoutMs}ms)` }
    }

    if (waitForSel) {
      // Delegate the post-reload selector wait to the iframe via the bridge —
      // we cannot access contentWindow.document from the parent for cross-origin frames.
      const waitResult = await executeViaPostMessageBridge(UiAutomationAction.WaitFor, {
        selector: waitForSel,
        timeoutMs,
      })
      if (!waitResult.success) {
        return { success: false, action, error: `Reloaded but timed out waiting for selector: ${waitForSel}` }
      }
    }

    return { success: true, action, details: 'Reloaded preview' }
  }

  // All other actions are implemented in automaton-api-preview.js inside the iframe.
  // Routing everything through the postMessage bridge means a single implementation
  // covers both same-origin and cross-origin frames — adding a new action only
  // requires adding it to UiAutomationAction and implementing it in automaton-api-preview.js.
  return executeViaPostMessageBridge(action, args)
}
