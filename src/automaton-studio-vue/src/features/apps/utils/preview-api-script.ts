import rawPreviewScript from '../scripts/automaton-api-preview.js?raw'
import { jsStr } from './js-str'

/**
 * Builds the automatonApi injection script for preview mode.
 * Injected into index.html before the app's own scripts load.
 */
export function buildPreviewApiScript(
  appId: string,
  baseUrl: string,
  token: string,
  testRunId: string,
  useMockBackend = true,
): string {
  const script = rawPreviewScript
    .replace(/__APP_ID__/g, jsStr(appId))
    .replace(/__BASE_URL__/g, jsStr(baseUrl))
    .replace(/__AUTH_TOKEN__/g, jsStr(token))
    .replace(/__TEST_RUN_ID__/g, jsStr(testRunId))
    .replace(/__USE_MOCK_BACKEND__/g, useMockBackend ? 'true' : 'false')
  return `<script id="__automaton_api__">\n${script}\n<\/script>`
}
