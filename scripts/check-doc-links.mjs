#!/usr/bin/env node
// Fails when a Markdown link inside the repository documentation points at a file or heading that does not exist.
// Internal links only: external URLs are never fetched. Run from anywhere: node scripts/check-doc-links.mjs
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const websiteDocs = path.join(root, 'src/automaton-vue/docs')
const scanned = [
  'README.md', 'AGENTS.md', 'CONTRIBUTING.md', 'SECURITY.md',
  'docs', 'src/automaton-vue/docs', 'src/Automaton.Api/Documentation', 'shared/ai-guidance',
]
// Historical pages keep the links they had; they are not checked.
const skipped = [path.join(root, 'docs/archive'), path.join(websiteDocs, 'node_modules'), path.join(websiteDocs, '.vitepress')]
// Site paths served by the website that are not Markdown pages.
const siteFiles = new Set(['/llms.txt', '/llms-full.txt'])

function* markdownFiles(entry) {
  const full = path.join(root, entry)
  if (!fs.existsSync(full)) return
  if (fs.statSync(full).isFile()) { yield full; return }
  for (const item of fs.readdirSync(full, { withFileTypes: true })) {
    const child = path.join(full, item.name)
    if (skipped.some(s => child === s)) continue
    if (item.isDirectory()) yield* markdownFiles(path.relative(root, child))
    else if (item.name.endsWith('.md')) yield child
  }
}

const withoutCode = text => text.replace(/^(```|~~~)[\s\S]*?^\1.*$/gm, '').replace(/`[^`\n]*`/g, '')
// Remove inline HTML tags until none are left, so nested or split tags cannot survive a single pass.
const stripTags = text => {
  let previous
  do { previous = text; text = text.replace(/<[^<>]*>/g, '') } while (text !== previous)
  return text
}
const slug = heading => stripTags(heading.trim().toLowerCase())
  .replace(/[`*_]/g, '').replace(/[^\p{L}\p{N}\s-]/gu, '').replace(/\s/g, '-')

const anchorCache = new Map()
function anchorsOf(file) {
  if (!anchorCache.has(file)) {
    const set = new Set()
    const seen = new Map()
    for (const m of withoutCode(fs.readFileSync(file, 'utf8')).matchAll(/^#{1,6}\s+(.+?)\s*#*$/gm)) {
      const base = slug(m[1])
      const n = seen.get(base) ?? 0
      seen.set(base, n + 1)
      set.add(n === 0 ? base : `${base}-${n}`)
    }
    anchorCache.set(file, set)
  }
  return anchorCache.get(file)
}

function resolveTarget(fromFile, target) {
  const base = target.startsWith('/')
    ? path.join(websiteDocs, target.replace(/^\/docs(?=\/|$)/, ''))
    : path.resolve(path.dirname(fromFile), target)
  for (const candidate of [base, `${base}.md`, path.join(base, 'index.md'), path.join(base, 'README.md')]) {
    if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) return candidate
  }
  return fs.existsSync(base) ? base : null
}

const problems = []
let checked = 0
for (const entry of scanned) {
  for (const file of markdownFiles(entry)) {
    const text = withoutCode(fs.readFileSync(file, 'utf8'))
    for (const m of text.matchAll(/\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g)) {
      const link = m[1].replace(/^<|>$/g, '')
      if (/^[a-z][a-z0-9+.-]*:/i.test(link) || link.startsWith('//')) continue
      const [pathPart, anchor] = link.split('#')
      checked++
      const rel = path.relative(root, file)
      if (siteFiles.has(pathPart)) continue
      if (pathPart === '' ) {
        if (anchor && !anchorsOf(file).has(anchor)) problems.push(`${rel}: missing heading #${anchor}`)
        continue
      }
      if (pathPart === '/docs' || pathPart === '/docs/' || pathPart === '/') continue
      const target = resolveTarget(file, decodeURI(pathPart))
      if (!target) { problems.push(`${rel}: broken link ${link}`); continue }
      if (anchor && target.endsWith('.md') && !anchorsOf(target).has(anchor)) {
        problems.push(`${rel}: ${pathPart} has no heading #${anchor}`)
      }
    }
  }
}

if (problems.length > 0) {
  console.error(`${problems.length} broken documentation link(s):`)
  for (const problem of problems) console.error(`  ${problem}`)
  process.exit(1)
}
console.log(`Documentation links OK (${checked} internal links checked).`)
