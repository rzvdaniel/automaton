// Fails the build when a prerendered marketing page lacks its real content or per-route meta tags
// (crawlers and link previews read this HTML).
import fs from 'node:fs'
import path from 'node:path'

const dist = path.resolve('dist')
const pages = [
  ['index.html', 'The home for every app your AI builds', 'https://automaton.monster/'],
  ['product/index.html', 'Five parts.', 'https://automaton.monster/product'],
  ['use-cases/index.html', 'Small apps that matter.', 'https://automaton.monster/use-cases'],
  ['catalog/index.html', 'Apps as source code.', 'https://automaton.monster/catalog'],
  ['how-setup-works/index.html', 'From a rented server to your first app.', 'https://automaton.monster/how-setup-works'],
  ['get-started/index.html', 'Pick how you want to begin', 'https://automaton.monster/get-started'],
  ['cli/index.html', 'Your AI already writes code.', 'https://automaton.monster/cli'],
  ['pricing/index.html', 'Own your installation.', 'https://automaton.monster/pricing'],
  ['404/index.html', 'Page not found', 'https://automaton.monster/404'],
]

let failed = false
for (const [file, text, canonical] of pages) {
  const full = path.join(dist, file)
  const html = fs.existsSync(full) ? fs.readFileSync(full, 'utf8') : ''
  const bodyStart = html.indexOf('<body')
  const body = bodyStart >= 0 ? html.slice(bodyStart) : ''
  const problems = []
  if (!body.includes(text)) problems.push(`body lacks "${text}"`)
  if (/<div id="app"><\/div>/.test(html)) problems.push('empty app root')
  if (!/<title>[^<]+<\/title>/.test(html)) problems.push('no <title>')
  if (!html.includes(`rel="canonical" href="${canonical}"`)) problems.push(`no canonical ${canonical}`)
  if (!html.includes('property="og:title"')) problems.push('no og:title')
  if (problems.length) {
    console.error(`Prerender check failed: ${file}: ${problems.join('; ')}`)
    failed = true
  }
}
if (failed) process.exit(1)
console.log('Prerender check passed')
