export interface ExecutionArtifact {
  available: boolean
  content?: string
  message?: string
}

function prettyResult(content: string): string {
  try {
    return JSON.stringify(JSON.parse(content), null, 2)
  } catch {
    return content
  }
}

/** Builds the text of the execution dialog: the run's logs and its result (many Actions only return a result). */
export function composeExecutionOutput(logs: ExecutionArtifact | null, result: ExecutionArtifact | null): string {
  const logText = logs?.available ? (logs.content ?? '').trimEnd() : ''
  const resultText = result?.available && result.content?.trim() ? prettyResult(result.content) : ''
  if (logText && resultText) return `Logs\n${logText}\n\nResult\n${resultText}`
  if (logText) return logText
  if (resultText) return `Result\n${resultText}`
  const reason = logs?.available === false ? logs.message : result?.available === false ? result.message : undefined
  return reason || 'This run produced no log output and no result.'
}
