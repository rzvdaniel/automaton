/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const read = (relative: string) => readFileSync(new URL(relative, import.meta.url), 'utf8')
const site = read('../../../../automaton-vue/src/assets/site.css')
const studio = read('./tokens.css')

/** The custom properties declared in the first block that starts with `selector {`. */
function declarations(css: string, selector: string): Record<string, string> {
  const start = css.indexOf(`${selector} {`)
  if (start < 0) throw new Error(`No ${selector} block`)
  const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start))
  return Object.fromEntries([...body.matchAll(/(--[\w-]+):\s*([^;]+);/g)].map((m) => [m[1]!, m[2]!.trim()]))
}

const names = ['--paper', '--panel', '--ink', '--ink-soft', '--line', '--signal', '--signal-ink', '--display', '--body', '--mono']

describe('Studio design tokens', () => {
  it('match the website in light mode', () => {
    const website = declarations(site, 'body.site-theme')
    const studioLight = declarations(studio, ':root')
    for (const name of names) expect(studioLight[name], name).toBe(website[name])
  })

  it('match the website in dark mode', () => {
    const websiteDark = declarations(site.slice(site.indexOf('prefers-color-scheme: dark')), 'body.site-theme')
    const studioDark = declarations(studio, '.app-dark')
    for (const name of names.filter((n) => n in studioDark)) expect(studioDark[name], name).toBe(websiteDark[name])
    expect(Object.keys(studioDark)).toEqual(expect.arrayContaining(['--paper', '--ink', '--line', '--signal']))
  })
})
