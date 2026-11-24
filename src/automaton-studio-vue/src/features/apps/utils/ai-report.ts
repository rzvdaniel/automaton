export interface AiReportInput {
  appName: string
  appId: string
  environment?: string
  version?: string
  /** "Run home-card" or "Deploy". */
  what: string
  at: string
  error: string
  logs?: string
  settingNames?: ReadonlyArray<string>
  secretNames?: ReadonlyArray<string>
}

export const LOG_LINES = 40

/** Blanks values that look like secrets (KEY=value, "token": "value", Bearer ..., URLs with passwords). Names stay. */
export function redactSecrets(text: string): string {
  return text
    .replace(/\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]{8,}/gi, '$1 [hidden]')
    .replace(/(:\/\/[^\s/:@]+:)[^\s/@]+@/g, '$1[hidden]@')
    .replace(/(["']?[\w.-]*(?:secret|token|password|passwd|pwd|api[_-]?key|apikey|authorization|credential|private[_-]?key|connectionstring)[\w.-]*["']?\s*[:=]\s*)("[^"]*"|'[^']*'|[^\s,;}&]+)/gi, '$1[hidden]')
}

/** Markdown an AI can start from: where, when, the error, the last log lines and the ask. Never values of secrets or settings. */
export function buildAiReport(input: AiReportInput): string {
  const lines = redactSecrets(input.logs ?? '').split(/\r?\n/).map((line) => line.trimEnd())
  while (lines.length && !lines[lines.length - 1]) lines.pop()
  const tail = lines.slice(-LOG_LINES)
  const names = (label: string, list?: ReadonlyArray<string>) => (list?.length ? [`- ${label} (names only): ${list.join(', ')}`] : [])
  return [
    `# Problem in Automaton app "${input.appName}"`,
    '',
    `- App: ${input.appName} (id ${input.appId})`,
    ...(input.environment ? [`- Environment: ${input.environment}`] : []),
    ...(input.version ? [`- Version: ${input.version}`] : []),
    `- What failed: ${input.what}`,
    `- Time: ${input.at}`,
    ...names('Settings', input.settingNames),
    ...names('Secrets', input.secretNames),
    '',
    '## Error',
    '',
    redactSecrets(input.error) || 'No error message was recorded.',
    ...(tail.length ? ['', `## Last ${tail.length} log lines`, '', '```', ...tail, '```'] : []),
    '',
    '## Ask',
    '',
    'Find the cause and fix it, then deploy and check it runs.',
    '',
  ].join('\n')
}
