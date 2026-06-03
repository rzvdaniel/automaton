import automatonCliRaw from '@/docs/pages/automaton-cli.md?raw'
import buildBackendAppRaw from '@/docs/pages/build-backend-app.md?raw'
import buildFrontendAppRaw from '@/docs/pages/build-frontend-app.md?raw'
import cliAiToolsRaw from '@/docs/pages/cli-ai-tools.md?raw'
import deployRaw from '@/docs/pages/deploy.md?raw'
import deployRunnerRaw from '@/docs/pages/deploy-runner.md?raw'
import gettingStartedRaw from '@/docs/pages/getting-started.md?raw'
import publishRaw from '@/docs/pages/publish.md?raw'

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
    group: 'Start here',
    content: gettingStartedRaw,
  },
  {
    slug: 'cli-ai-tools',
    label: 'Build with AI Tools',
    summary: 'Recommended local workflow',
    group: 'Start here',
    content: cliAiToolsRaw,
  },
  {
    slug: 'build-frontend-app',
    label: 'Build Frontend App',
    summary: 'Static apps hosted by Automaton',
    group: 'Start here',
    content: buildFrontendAppRaw,
  },
  {
    slug: 'build-backend-app',
    label: 'Build Backend App',
    summary: 'Python APIs running on a Runner',
    group: 'Start here',
    content: buildBackendAppRaw,
  },
  {
    slug: 'deploy-runner',
    label: 'Deploy Runner',
    summary: 'Connect backend execution',
    group: 'Start here',
    content: deployRunnerRaw,
  },
  {
    slug: 'deploy',
    label: 'Deploy App',
    summary: 'Ship the current app',
    group: 'Start here',
    content: deployRaw,
  },
  {
    slug: 'publish',
    label: 'Publish App',
    summary: 'Make the app public',
    group: 'Start here',
    content: publishRaw,
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
