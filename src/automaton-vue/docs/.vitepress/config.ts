import { defineConfig } from 'vitepress'

const isDev = process.env.NODE_ENV !== 'production'
const siteOrigin = isDev ? 'http://localhost:5174' : 'https://automaton.monster'

export default defineConfig({
  srcDir: '.',
  base: '/docs/',
  title: 'Automaton Monster',
  description: 'Documentation for building, deploying, and publishing apps with Automaton Monster.',
  lang: 'en-US',
  cleanUrls: true,
  outDir: '../dist/docs',
  sitemap: {
    hostname: 'https://automaton.monster/docs',
  },
  head: [
    ['link', { rel: 'icon', href: '/favicon.ico' }],
    ['meta', { name: 'theme-color', content: '#06C46F' }],
  ],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Apps', link: `${siteOrigin}/apps` },
      { text: 'Docs', link: '/' },
      { text: 'CLI', link: `${siteOrigin}/cli` },
      { text: 'Pricing', link: `${siteOrigin}/pricing` },
    ],
    socialLinks: [{ icon: 'github', link: 'https://github.com' }],
    search: {
      provider: 'local',
    },
    sidebar: [
      {
        text: 'Start Here',
        items: [
          { text: 'Build an Automaton App', link: '/build-an-automaton-app' },
          { text: 'Getting Started', link: '/getting-started' },
          { text: 'Build with AI Tools', link: '/cli-ai-tools' },
          { text: 'Build a Frontend App', link: '/build-frontend-app' },
          { text: 'Build a Backend App', link: '/build-backend-app' },
          { text: 'Deploy a Runner', link: '/deploy-runner' },
          { text: 'Deploy', link: '/deploy' },
          { text: 'Publish', link: '/publish' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'Automaton CLI', link: '/automaton-cli' },
          { text: 'automaton.json Reference', link: '/automaton-json' },
          { text: 'Full-Stack Todo Example', link: '/example-todo-fullstack' },
        ],
      },
    ],
    footer: {
      message: 'Automaton Monster public docs',
      copyright: 'Copyright © 2026 Automaton',
    },
    outline: {
      level: [2, 3],
    },
  },
})
