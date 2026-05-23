import appEditorRaw from '@/docs/pages/app-editor.md?raw'
import automatonCliRaw from '@/docs/pages/automaton-cli.md?raw'
import cliAiToolsRaw from '@/docs/pages/cli-ai-tools.md?raw'
import deployRaw from '@/docs/pages/deploy.md?raw'
import gettingStartedRaw from '@/docs/pages/getting-started.md?raw'
import publishRaw from '@/docs/pages/publish.md?raw'
import runtimeArchitectureRaw from '@/docs/pages/runtime-architecture.md?raw'
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
    summary: '5-minute quickstart',
    group: 'Getting Started',
    content: gettingStartedRaw,
  },
  {
    slug: 'cli-ai-tools',
    label: 'Build with AI Tools',
    summary: 'Recommended local workflow',
    group: 'Getting Started',
    content: cliAiToolsRaw,
  },
  {
    slug: 'stack-selection',
    label: 'Choose a Stack',
    summary: 'Plain HTML, React, or Vue',
    group: 'Getting Started',
    content: stackSelectionRaw,
  },
  {
    slug: 'deploy',
    label: 'Deploy',
    summary: 'Ship the current app',
    group: 'Getting Started',
    content: deployRaw,
  },
  {
    slug: 'publish',
    label: 'Publish',
    summary: 'Make the app public',
    group: 'Getting Started',
    content: publishRaw,
  },
  {
    slug: 'app-editor',
    label: 'App Editor',
    summary: 'Browser-first workflow',
    group: 'Getting Started',
    content: appEditorRaw,
  },
  {
    slug: 'troubleshooting',
    label: 'Troubleshooting',
    summary: 'Common errors and fixes',
    group: 'Advanced',
    content: troubleshootingRaw,
  },
  {
    slug: 'automaton-cli',
    label: 'CLI Reference',
    summary: 'Commands and options',
    group: 'Advanced',
    content: automatonCliRaw,
  },
  {
    slug: 'runtime-architecture',
    label: 'Runtime Architecture',
    summary: 'Advanced storage and runtime model',
    group: 'Advanced',
    content: runtimeArchitectureRaw,
  },
]

export const defaultDocSlug = 'getting-started'
