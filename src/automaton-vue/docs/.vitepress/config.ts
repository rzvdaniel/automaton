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
        text: 'Tasks',
        items: [
          { text: 'Is Automaton for you?', link: '/is-automaton-for-you' },
          { text: 'Install the server', link: '/install-server' },
          { text: 'Create the first administrator', link: '/first-admin' },
          { text: 'Add a Runner', link: '/deploy-runner' },
          { text: 'Create your first app', link: '/getting-started' },
          { text: 'Build an Automaton app', link: '/build-an-automaton-app' },
          { text: 'Deploy and publish an app', link: '/deploy' },
          { text: 'Publish your own app catalog', link: '/apps-repository' },
          { text: 'Update the server', link: '/update-server' },
          { text: 'Back up and restore', link: '/backup-restore' },
          { text: 'Secrets and permissions', link: '/secrets-and-permissions' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'Automaton CLI', link: '/automaton-cli' },
          { text: 'automaton.json', link: '/automaton-json' },
          { text: 'Troubleshooting', link: '/troubleshooting' },
          { text: 'Security overview', link: '/security' },
          { text: 'Current limitations', link: '/limitations' },
          { text: 'FAQ', link: '/faq' },
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
