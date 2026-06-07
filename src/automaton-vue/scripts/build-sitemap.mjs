import fs from 'node:fs'
import path from 'node:path'

const distRoot = path.resolve('dist')
const mainSitemapPath = path.join(distRoot, 'sitemap.xml')
const docsSitemapPath = path.join(distRoot, 'docs', 'sitemap.xml')
const siteUrl = 'https://automaton.monster'

function readFile(filePath) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`Required sitemap file not found: ${filePath}`)
  }

  return fs.readFileSync(filePath, 'utf8')
}

function extractUrls(xml) {
  const matches = xml.matchAll(/<loc>([^<]+)<\/loc>/g)
  return Array.from(matches, (match) => match[1]).filter(Boolean)
}

function normalizeDocsUrl(url) {
  if (url === siteUrl || url === `${siteUrl}/` || url === `${siteUrl}/docs` || url === `${siteUrl}/docs/`) {
    return `${siteUrl}/docs`
  }

  if (url.startsWith(`${siteUrl}/docs/`)) {
    return url
  }

  if (url.startsWith(`${siteUrl}/`)) {
    return `${siteUrl}/docs${url.slice(siteUrl.length)}`
  }

  return url
}

const mainUrls = extractUrls(readFile(mainSitemapPath))
const docsUrls = extractUrls(readFile(docsSitemapPath)).map(normalizeDocsUrl)
const allUrls = Array.from(new Set([...mainUrls, ...docsUrls]))

const combinedXml = [
  '<?xml version="1.0" encoding="UTF-8"?>',
  '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
  ...allUrls.map((url) => `  <url><loc>${url}</loc></url>`),
  '</urlset>',
  '',
].join('\n')

fs.writeFileSync(mainSitemapPath, combinedXml)
fs.rmSync(docsSitemapPath, { force: true })
