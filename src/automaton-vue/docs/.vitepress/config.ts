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
      { text: 'Open source', link: `${siteOrigin}/pricing` },
    ],
    socialLinks: [{ icon: 'github', link: 'https://github.com/rzvdaniel/automaton' }],
    search: {
      provider: 'local',
    },
    sidebar: [
      {
        text: 'Start here',
        items: [
          { text: 'What is Automaton', link: '/what-is-automaton' },
          { text: 'Work with AI', link: '/work-with-ai' },
          { text: 'A tour of Studio', link: '/studio-tour' },
          { text: 'Install Automaton', link: '/install' },
          { text: 'Build an app', link: '/build-an-app' },
          { text: 'CLI recipes', link: '/cli' },
          { text: 'When something goes wrong', link: '/when-something-goes-wrong' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'Back up and restore', link: '/backup-restore' },
          { text: 'Security overview', link: '/security' },
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
