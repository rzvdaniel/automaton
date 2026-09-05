import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'path'
import fs from 'fs'

const sharedAiGuidanceRoot = path.resolve(__dirname, '../../shared/ai-guidance')
const sharedAiGuidanceSeedRoot = path.join(sharedAiGuidanceRoot, 'seed')
const faviconAssets = [
  { url: '/logo.svg', file: path.resolve(__dirname, './src/assets/logo.svg'), type: 'image/svg+xml' },
  { url: '/favicon.svg', file: path.resolve(__dirname, './src/assets/logo.svg'), type: 'image/svg+xml' },
  { url: '/favicon.ico', file: path.resolve(__dirname, './src/assets/favicons/favicon.ico'), type: 'image/x-icon' },
  { url: '/favicon-32x32.png', file: path.resolve(__dirname, './src/assets/favicons/favicon-32x32.png'), type: 'image/png' },
  { url: '/favicon-48x48.png', file: path.resolve(__dirname, './src/assets/favicons/favicon-48x48.png'), type: 'image/png' },
  { url: '/android-chrome-192x192.png', file: path.resolve(__dirname, './src/assets/favicons/favicon-192x192.png'), type: 'image/png' },
  { url: '/android-chrome-512x512.png', file: path.resolve(__dirname, './src/assets/favicons/favicon-512x512.png'), type: 'image/png' },
  { url: '/apple-touch-icon.png', file: path.resolve(__dirname, './src/assets/favicons/apple-touch-icon.png'), type: 'image/png' },
]
const siteManifest = JSON.stringify(
  {
    name: 'Automaton Monster',
    short_name: 'Automaton',
    icons: [
      { src: '/android-chrome-192x192.png', sizes: '192x192', type: 'image/png' },
      { src: '/android-chrome-512x512.png', sizes: '512x512', type: 'image/png' },
    ],
    theme_color: '#06C46F',
    background_color: '#05070a',
    display: 'standalone',
  },
  null,
  2,
)

function stableLogoPlugin() {
  return {
    name: 'stable-logo-plugin',
    transformIndexHtml: {
      order: 'post',
      handler() {
        return [
          {
            tag: 'link',
            attrs: { rel: 'icon', href: '/favicon.ico', sizes: 'any' },
            injectTo: 'head',
          },
          {
            tag: 'link',
            attrs: { rel: 'icon', href: '/favicon.svg', type: 'image/svg+xml' },
            injectTo: 'head',
          },
          {
            tag: 'link',
            attrs: { rel: 'icon', href: '/favicon-32x32.png', type: 'image/png', sizes: '32x32' },
            injectTo: 'head',
          },
          {
            tag: 'link',
            attrs: { rel: 'icon', href: '/favicon-48x48.png', type: 'image/png', sizes: '48x48' },
            injectTo: 'head',
          },
          {
            tag: 'link',
            attrs: { rel: 'apple-touch-icon', href: '/apple-touch-icon.png' },
            injectTo: 'head',
          },
          {
            tag: 'link',
            attrs: { rel: 'manifest', href: '/site.webmanifest' },
            injectTo: 'head',
          },
          {
            tag: 'meta',
            attrs: { name: 'theme-color', content: '#06C46F' },
            injectTo: 'head',
          },
        ]
      },
    },
    configureServer(server: any) {
      for (const asset of faviconAssets) {
        server.middlewares.use(asset.url, (_req: any, res: any) => {
          res.setHeader('Content-Type', asset.type)
          res.end(fs.readFileSync(asset.file))
        })
      }
      server.middlewares.use('/site.webmanifest', (_req: any, res: any) => {
        res.setHeader('Content-Type', 'application/manifest+json')
        res.end(siteManifest)
      })
    },
    generateBundle() {
      for (const asset of faviconAssets) {
        this.emitFile({
          type: 'asset',
          fileName: asset.url.replace(/^\//, ''),
          source: fs.readFileSync(asset.file),
        })
      }
      this.emitFile({
        type: 'asset',
        fileName: 'site.webmanifest',
        source: siteManifest,
      })
    },
  }
}

function sharedAiGuidanceDevPlugin() {
  const resolvePublicGuidanceFiles = () => {
    const { llmsText, llmsFullText } = buildPublicLlmsAssets()
    return [
      { url: '/llms.txt', content: llmsText, type: 'text/plain; charset=utf-8' },
      { url: '/llms-full.txt', content: llmsFullText, type: 'text/plain; charset=utf-8' },
    ]
  }

  return {
    name: 'shared-ai-guidance-dev-plugin',
    configureServer(server: any) {
      for (const route of resolvePublicGuidanceFiles()) {
        server.middlewares.use(route.url, (_req: any, res: any) => {
          res.setHeader('Content-Type', route.type)
          res.end(route.content)
        })
      }

      server.middlewares.use((req: any, res: any, next: any) => {
        const requestPath = req.url?.split('?')[0] ?? ''

        if (!requestPath.startsWith('/llms/')) {
          next()
          return
        }

        const requestedFileName = path.posix.basename(requestPath)
        const absolutePath = findSeedGuidanceFile(requestedFileName)

        if (!absolutePath || !fs.existsSync(absolutePath) || !fs.statSync(absolutePath).isFile()) {
          next()
          return
        }

        const extension = path.extname(absolutePath).toLowerCase()
        const contentType =
          extension === '.md'
            ? 'text/markdown; charset=utf-8'
            : extension === '.txt'
              ? 'text/plain; charset=utf-8'
              : 'application/octet-stream'

        res.setHeader('Content-Type', contentType)
        res.end(fs.readFileSync(absolutePath))
      })
    },
  }
}

function emitSharedAiGuidanceAssetsPlugin() {
  return {
    name: 'emit-shared-ai-guidance-assets-plugin',
    generateBundle() {
      const { llmsText, llmsFullText } = buildPublicLlmsAssets()

      this.emitFile({
        type: 'asset',
        fileName: 'llms.txt',
        source: llmsText,
      })

      this.emitFile({
        type: 'asset',
        fileName: 'llms-full.txt',
        source: llmsFullText,
      })

      for (const document of listSeedGuidanceDocuments()) {
        this.emitFile({
          type: 'asset',
          fileName: path.posix.join('llms', path.basename(document.filePath)),
          source: fs.readFileSync(document.filePath, 'utf-8'),
        })
      }
    },
  }
}

function listSeedGuidanceDocuments() {
  const files: Array<{ filePath: string; surface: string }> = []
  const guidanceSurfaces = ['shared', 'studio', 'cli', 'mcp']

  for (const surface of guidanceSurfaces) {
    const guidanceRoot = path.join(sharedAiGuidanceSeedRoot, surface, 'guidance')
    if (!fs.existsSync(guidanceRoot)) {
      continue
    }

    for (const entry of fs.readdirSync(guidanceRoot, { withFileTypes: true })) {
      if (entry.isFile() && entry.name.endsWith('.md')) {
        files.push({ filePath: path.join(guidanceRoot, entry.name), surface })
      }
    }
  }

  return files.sort((a, b) => a.filePath.localeCompare(b.filePath))
}

function readSharedBasePrompt() {
  const basePath = path.join(sharedAiGuidanceSeedRoot, 'shared', 'base', 'automaton-app-contract.md')
  return fs.readFileSync(basePath, 'utf-8').trim()
}

function readPublicBootstrap() {
  const bootstrapPath = path.join(sharedAiGuidanceRoot, 'public', 'automaton-app-bootstrap.md')
  return fs.readFileSync(bootstrapPath, 'utf-8').trim()
}

function buildPublicLlmsAssets() {
  const bootstrap = readPublicBootstrap()
  const basePrompt = readSharedBasePrompt()
  const guidanceDocuments = listSeedGuidanceDocuments()
  const llmsTextLines = [
    bootstrap,
    '',
    basePrompt,
    '',
    '## Linked guidance',
    '',
    ...guidanceDocuments.map(document => `- \`llms/${path.basename(document.filePath)}\``),
  ]

  const llmsFullSections = [
    llmsTextLines.join('\n').trim(),
    ...guidanceDocuments.map(document => fs.readFileSync(document.filePath, 'utf-8').trim()).flatMap(content => ['', '---', '', content]),
  ]

  return {
    llmsText: llmsTextLines.join('\n').trim() + '\n',
    llmsFullText: llmsFullSections.join('\n').trim() + '\n',
  }
}

function findSeedGuidanceFile(fileName: string) {
  const match = listSeedGuidanceDocuments().find(document => path.basename(document.filePath) === fileName)
  return match?.filePath ?? null
}

function docsRedirectDevPlugin() {
  return {
    name: 'docs-redirect-dev-plugin',
    configureServer(server: any) {
      server.middlewares.use((req: any, res: any, next: any) => {
        const requestPath = req.url?.split('?')[0] ?? ''

        if (requestPath !== '/docs') {
          next()
          return
        }

        res.statusCode = 302
        res.setHeader('Location', '/docs/')
        res.end()
      })
    },
  }
}

export default defineConfig({
  plugins: [vue(), stableLogoPlugin(), sharedAiGuidanceDevPlugin(), emitSharedAiGuidanceAssetsPlugin(), docsRedirectDevPlugin()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5174,
    proxy: {
      '/docs': {
        target: 'http://127.0.0.1:5175',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
})
