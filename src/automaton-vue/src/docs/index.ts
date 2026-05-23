import appEditorRaw from '@/docs/pages/app-editor.md?raw'
import automatonCliRaw from '@/docs/pages/automaton-cli.md?raw'
import cliAiToolsRaw from '@/docs/pages/cli-ai-tools.md?raw'
import gettingStartedRaw from '@/docs/pages/getting-started.md?raw'
import stackSelectionRaw from '@/docs/pages/stack-selection.md?raw'
import troubleshootingRaw from '@/docs/pages/troubleshooting.md?raw'

export type DocItem = {
  slug: string
  label: string
  summary: string
  group: string
  content: string
}

export const docs: DocItem[] = [
  {
    slug: 'getting-started',
    label: 'Getting Started',
    summary: 'Current app model and core workflow',
    group: 'Start',
    content: gettingStartedRaw,
  },
  {
    slug: 'cli-ai-tools',
    label: 'Build with AI',
    summary: 'Recommended local workflow',
    group: 'Build',
    content: cliAiToolsRaw,
  },
  {
    slug: 'app-editor',
    label: 'App Editor',
    summary: 'Browser-first workflow',
    group: 'Build',
    content: appEditorRaw,
  },
  {
    slug: 'stack-selection',
    label: 'Choose a Stack',
    summary: 'Plain HTML, React, or Vue',
    group: 'Build',
    content: stackSelectionRaw,
  },
  {
    slug: 'troubleshooting',
    label: 'Troubleshooting',
    summary: 'Common errors and fixes',
    group: 'Reference',
    content: troubleshootingRaw,
  },
  {
    slug: 'automaton-cli',
    label: 'CLI Reference',
    summary: 'Commands and options',
    group: 'Reference',
    content: automatonCliRaw,
  },
]

export const defaultDocSlug = 'getting-started'
