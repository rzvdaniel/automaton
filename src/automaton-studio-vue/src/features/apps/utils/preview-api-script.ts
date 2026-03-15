import rawPreviewScript from '../scripts/automaton-api-preview.js?raw'
import { jsStr } from './js-str'

/**
 * Builds the automatonApi injection script for preview mode.
 * Injected into index.html before the app's own scripts load.
 */
export function buildPreviewApiScript(appId: string, baseUrl: string, token: string): string {
  const script = rawPreviewScript
    .replace(/__APP_ID__/g, jsStr(appId))
    .replace(/__BASE_URL__/g, jsStr(baseUrl))
    .replace(/__AUTH_TOKEN__/g, jsStr(token))
  return `<script id="__automaton_api__">\n${script}\n<\/script>`
}
