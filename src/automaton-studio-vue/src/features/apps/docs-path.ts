/** Resolves a relative link of a docs page to another file under the app's `docs/` folder, or null when it leaves it. */
export function resolveDocsPath(currentPath: string, href: string): string | null {
  const target = href.split('#')[0]!.split('?')[0]!
  if (!target) return null
  const parts = target.startsWith('/') ? [] : currentPath.split('/').slice(0, -1)
  for (const part of target.split('/')) {
    if (part === '' || part === '.') continue
    if (part === '..') parts.pop()
    else parts.push(part)
  }
  const resolved = parts.join('/')
  return resolved.startsWith('docs/') ? resolved : null
}
