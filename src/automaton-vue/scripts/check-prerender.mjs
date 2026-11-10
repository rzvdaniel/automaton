// Fails the build when a prerendered marketing page lacks its real content (crawlers and link previews read this HTML).
import fs from 'node:fs'
import path from 'node:path'

const dist = path.resolve('dist')
const pages = [
  ['index.html', 'The home for every app your AI builds'],
  ['product/index.html', 'Five parts.'],
  ['pricing/index.html', 'Own your installation.'],
  ['get-started/index.html', 'Pick how you want to begin'],
]

let failed = false
for (const [file, text] of pages) {
  const full = path.join(dist, file)
  const html = fs.existsSync(full) ? fs.readFileSync(full, 'utf8') : ''
  const body = html.slice(html.indexOf('<body'))
  if (!body.includes(text) || /<div id="app"><\/div>/.test(html)) {
    console.error(`Prerender check failed: ${file} does not contain "${text}" in its HTML`)
    failed = true
  }
}
if (failed) process.exit(1)
console.log('Prerender check passed')
