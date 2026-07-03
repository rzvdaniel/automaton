export const UiAutomationAction = {
  Navigate: 'ui_navigate',
  Reload: 'ui_reload',
  Click: 'ui_click',
  Type: 'ui_type',
  ReadText: 'ui_read_text',
  WaitFor: 'ui_wait_for',
  Assert: 'ui_assert',
  GetPageSnapshot: 'ui_get_page_snapshot',
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

const APPLICATION_AUTOMATION_TIMEOUT_MS = 12000

function getApplicationIframe(): HTMLIFrameElement | null {
  return (
    document.querySelector<HTMLIFrameElement>('.build-visual-workspace .application-iframe')
  )
}

async function executeViaPostMessageBridge(
  action: UiAutomationAction,
  args: Record<string, unknown>,
): Promise<UiAutomationResult> {
  const iframe = getApplicationIframe()
  const targetWindow = iframe?.contentWindow
  if (!targetWindow) {
    return { success: false, action, error: 'Application iframe is not available.' }
  }

  const requestId = crypto.randomUUID()
  const timeoutMs = Math.max(
    APPLICATION_AUTOMATION_TIMEOUT_MS,
    Number(args.timeoutMs ?? 0) + 2000,
  )

  return new Promise<UiAutomationResult>((resolve) => {
    const timeoutHandle = window.setTimeout(() => {
      window.removeEventListener('message', onMessage)
      resolve({
        success: false,
        action,
        error: `Timed out waiting for application automation response (${timeoutMs}ms).`,
      })
    }, timeoutMs)

    function onMessage(event: MessageEvent) {
      const data = event.data as {
        type?: string
        requestId?: string
        result?: UiAutomationResult
      }

      if (data?.type !== 'applicationAutomationResponse' || data.requestId !== requestId) {
        return
      }

      window.clearTimeout(timeoutHandle)
      window.removeEventListener('message', onMessage)
      resolve(
        data.result ?? {
          success: false,
          action,
          error: 'Application automation returned an empty response.',
        },
      )
    }

    window.addEventListener('message', onMessage)

    targetWindow.postMessage(
      {
        type: 'applicationAutomationRequest',
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
): Promise<UiAutomationResult> {
  // Reload needs to listen to the iframe load event from the parent side —
  // it cannot be detected from within the iframe itself.
  if (action === UiAutomationAction.Reload) {
    const waitForSel = args.waitForSelector !== undefined ? String(args.waitForSelector).trim() : ''
    const timeoutMs = Number(args.timeoutMs ?? 5000)

    const iframeEl = getApplicationIframe()
    if (!iframeEl) {
      return { success: false, action, error: 'Application iframe is not available' }
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

    return { success: true, action, details: 'Reloaded application' }
  }

  // All other actions are implemented in the injected application runtime script inside the iframe.
  // Routing everything through the postMessage bridge means a single implementation
  // covers both same-origin and cross-origin frames — adding a new action only
  // requires adding it to UiAutomationAction and implementing it in the runtime script.
  return executeViaPostMessageBridge(action, args)
}
