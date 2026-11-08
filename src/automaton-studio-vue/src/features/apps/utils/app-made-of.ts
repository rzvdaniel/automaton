import type { App } from '../types/apps.types'
import { isActionOnlyApp } from './app-operational-summary'

export interface MadeOfItem {
  key: 'runs' | 'source' | 'data' | 'secrets' | 'backup'
  label: string
  value: string
  detail: string
}

type MadeOfApp = Pick<App, 'application' | 'developmentMode' | 'runtimeSource' | 'forkedFromAppName'>

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`
}

/** The five facts the app page's "What this app is made of" strip shows. */
export function getMadeOf(app: MadeOfApp): MadeOfItem[] {
  const { application } = app
  const isStatic = application.isStaticSite === true
  const actionOnly = isActionOnlyApp(app)
  const mounts = application.storage?.mounts?.length ?? 0
  const secrets = application.secrets?.length ?? 0

  const runs = application.compose
    ? { value: 'Docker Compose', detail: 'A set of containers on a Runner' }
    : isStatic
      ? { value: 'Static site', detail: 'Files served as they are' }
      : actionOnly
        ? { value: 'Actions only', detail: 'No web page; runs when started' }
        : { value: 'Web app', detail: application.runtime?.command?.executable ?? 'Runs on a Runner' }

  const source = app.forkedFromAppName
    ? { value: 'App store', detail: `Installed from ${app.forkedFromAppName}` }
    : app.developmentMode === 'local' || app.runtimeSource !== 'studio'
      ? { value: 'Local project', detail: 'Deployed with the CLI' }
      : { value: 'Studio', detail: 'Edited and built here' }

  const dataDirectory = application.storage?.dataDirectory ?? ''
  const data = isStatic
    ? { value: 'None', detail: 'A static site keeps no data' }
    : mounts > 0
      ? { value: plural(mounts, 'folder', 'folders'), detail: `Kept in ${dataDirectory || 'the data folder'}` }
      : dataDirectory
        ? { value: 'Data folder', detail: dataDirectory }
        : { value: 'None', detail: 'Nothing stored' }

  const secretsItem = secrets > 0
    ? { value: plural(secrets, 'secret', 'secrets'), detail: 'Declared by the app' }
    : { value: 'None', detail: 'The app declares no secrets' }

  const backup = isStatic || data.value === 'None'
    ? { value: 'Nothing to back up', detail: isStatic ? 'Files come from the source' : 'No data is kept' }
    : { value: 'Not scheduled', detail: 'Data stays on its Runner' }

  return [
    { key: 'runs', label: 'Runs as', ...runs },
    { key: 'source', label: 'Source', ...source },
    { key: 'data', label: 'Data', ...data },
    { key: 'secrets', label: 'Secrets', ...secretsItem },
    { key: 'backup', label: 'Backup', ...backup },
  ]
}
