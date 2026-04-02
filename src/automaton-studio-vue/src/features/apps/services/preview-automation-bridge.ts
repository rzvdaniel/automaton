import type { UiLogEntry } from '../types/console.types'

export const UiAutomationAction = {
  Navigate: 'ui_navigate',
  Click: 'ui_click',
  Type: 'ui_type',
  ReadText: 'ui_read_text',
  WaitFor: 'ui_wait_for',
  Assert: 'ui_assert',
  GetPageSnapshot: 'ui_get_page_snapshot',
  GetConsoleLogs: 'ui_get_console_logs',
} as const

export type UiAutomationAction = typeof UiAutomationAction[keyof typeof UiAutomationAction]

export interface UiAutomationResult {
  success: boolean
  action: UiAutomationAction
  details?: string
  value?: string
  logs?: Array<Pick<UiLogEntry, 'timestamp' | 'level' | 'source' | 'message'>>
  snapshot?: {
    title: string
    url: string
    bodyText: string
  }
  error?: string
}

const PREVIEW_AUTOMATION_TIMEOUT_MS = 12000

function getPreviewIframe(): HTMLIFrameElement | null {
  return (
    document.querySelector<HTMLIFrameElement>('.build-visual-workspace .preview-iframe')
    ?? document.querySelector<HTMLIFrameElement>('.preview-overlay .preview-iframe')
  )
}

function getPreviewDocument(): Document | null {
  const iframe = getPreviewIframe()
  if (!iframe?.contentWindow) {
    return null
  }

  try {
    return iframe.contentWindow.document
  } catch {
    // Cross-origin frame; direct DOM access is blocked. Caller should fallback to postMessage bridge.
    return null
  }
}

function isCrossOriginFrame(iframe: HTMLIFrameElement | null): boolean {
  if (!iframe?.contentWindow) {
    return false
  }

  try {
    void iframe.contentWindow.document
    return false
  } catch {
    return true
  }
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

async function waitForSelector(
  doc: Document,
  selector: string,
  timeoutMs: number,
  intervalMs: number,
): Promise<Element | null> {
  const start = Date.now()
  while (Date.now() - start < timeoutMs) {
    const found = doc.querySelector(selector)
    if (found) {
      return found
    }
    await new Promise((resolve) => setTimeout(resolve, intervalMs))
  }
  return null
}

export async function executeUiAutomationAction(
  action: UiAutomationAction,
  args: Record<string, unknown>,
  getUiLogs: () => UiLogEntry[],
): Promise<UiAutomationResult> {
  const iframe = getPreviewIframe()
  const requiresDomAccess = action !== UiAutomationAction.GetConsoleLogs
  const useBridge = requiresDomAccess && isCrossOriginFrame(iframe)

  if (useBridge) {
    return executeViaPostMessageBridge(action, args)
  }

  const doc = getPreviewDocument()

  if (action !== UiAutomationAction.GetConsoleLogs && !doc) {
    return { success: false, action, error: 'Preview is not ready. Open Build > Visual mode first.' }
  }

  try {
    switch (action) {
      case UiAutomationAction.Navigate: {
        const url = String(args.url ?? '').trim()
        if (!url) return { success: false, action, error: "Missing required argument 'url'" }
        const win = doc!.defaultView
        if (!win) return { success: false, action, error: 'Preview window is not available' }
        win.location.assign(url)
        return { success: true, action, details: `Navigated to ${url}` }
      }
      case UiAutomationAction.Click: {
        const selector = String(args.selector ?? '').trim()
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" }
        const element = doc!.querySelector<HTMLElement>(selector)
        if (!element) return { success: false, action, error: `Element not found for selector: ${selector}` }
        element.click()
        return { success: true, action, details: `Clicked ${selector}` }
      }
      case UiAutomationAction.Type: {
        const selector = String(args.selector ?? '').trim()
        const text = String(args.text ?? '')
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" }
        const element = doc!.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)
        if (!element) return { success: false, action, error: `Element not found for selector: ${selector}` }
        element.focus()
        element.value = text
        element.dispatchEvent(new Event('input', { bubbles: true }))
        element.dispatchEvent(new Event('change', { bubbles: true }))
        return { success: true, action, details: `Typed into ${selector}` }
      }
      case UiAutomationAction.ReadText: {
        const selector = String(args.selector ?? '').trim()
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" }
        const element = doc!.querySelector<HTMLElement>(selector)
        if (!element) return { success: false, action, error: `Element not found for selector: ${selector}` }
        const value = (element.textContent ?? '').trim()
        return { success: true, action, value, details: `Read text from ${selector}` }
      }
      case UiAutomationAction.WaitFor: {
        const selector = String(args.selector ?? '').trim()
        const timeoutMs = Number(args.timeoutMs ?? 3000)
        const intervalMs = Number(args.intervalMs ?? 100)
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" }
        const found = await waitForSelector(doc!, selector, timeoutMs, intervalMs)
        if (!found) {
          return {
            success: false,
            action,
            error: `Timed out waiting for selector: ${selector} (${timeoutMs}ms)`,
          }
        }
        return { success: true, action, details: `Selector appeared: ${selector}` }
      }
      case UiAutomationAction.Assert: {
        const selector = String(args.selector ?? '').trim()
        const exists = args.exists === undefined ? true : Boolean(args.exists)
        const containsText = args.containsText === undefined ? undefined : String(args.containsText)
        const equalsText = args.equalsText === undefined ? undefined : String(args.equalsText)
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" }

        const element = doc!.querySelector<HTMLElement>(selector)
        const found = Boolean(element)
        if (exists !== found) {
          return {
            success: false,
            action,
            error: `Assertion failed for ${selector}: expected exists=${exists}, actual=${found}`,
          }
        }

        if (element && containsText !== undefined) {
          const text = (element.textContent ?? '').trim()
          if (!text.includes(containsText)) {
            return {
              success: false,
              action,
              error: `Assertion failed for ${selector}: text does not include "${containsText}"`,
            }
          }
        }

        if (element && equalsText !== undefined) {
          const text = (element.textContent ?? '').trim()
          if (text !== equalsText) {
            return {
              success: false,
              action,
              error: `Assertion failed for ${selector}: expected "${equalsText}", got "${text}"`,
            }
          }
        }

        return { success: true, action, details: `Assertion passed for ${selector}` }
      }
      case UiAutomationAction.GetPageSnapshot: {
        const maxChars = Math.max(100, Number(args.maxChars ?? 1000))
        const title = doc!.title ?? ''
        const url = doc!.location?.href ?? ''
        const bodyText = (doc!.body?.innerText ?? '').slice(0, maxChars)
        return {
          success: true,
          action,
          details: `Snapshot captured (${bodyText.length} chars)`,
          snapshot: { title, url, bodyText },
        }
      }
      case UiAutomationAction.GetConsoleLogs: {
        const logs = getUiLogs().slice(0, 50).map((entry) => ({
          timestamp: entry.timestamp,
          level: entry.level,
          source: entry.source,
          message: entry.message,
        }))
        return { success: true, action, logs, details: `Collected ${logs.length} log entries` }
      }
      default:
        return { success: false, action, error: `Unsupported UI action: ${action}` }
    }
  } catch (error) {
    return {
      success: false,
      action,
      error: error instanceof Error ? error.message : 'Unknown UI automation error',
    }
  }
}

