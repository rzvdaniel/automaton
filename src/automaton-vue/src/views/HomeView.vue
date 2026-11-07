<script setup lang="ts">
import { RouterLink } from 'vue-router'
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import BFinal from '@/components/site/b/BFinal.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useSiteTheme } from '@/composables/useSiteTheme'
import '@/assets/site-b.css'
import { computed, ref } from 'vue'

const audiences = [
  { h: 'Creators', p: 'Build small things with your AI and keep them in one place.', l: ['Every idea gets its own link in minutes', 'Publish the good ones to your app store', 'One small server instead of many subscriptions'], q: 'I build a new tool most weekends. They all live on one small server, and my friends open them from my store.' },
  { h: 'Teams', p: 'Internal tools, dashboards and jobs your team actually uses.', l: ['Sign-in and per-app access for your people', 'A shared catalog to install tools from', 'Scheduled jobs and backups included'], q: 'Our team catalog has twelve tools. New people sign in and install what they need on day one.' },
  { h: 'Organizations', p: 'Keep apps and data in-house, with costs you can predict.', l: ['Data stays on servers you choose, in the country you choose', 'Administrators approve what each app may access', 'Your own app store, inside or outside the company'], q: 'We needed AI-built apps without sending data to another cloud. Automaton runs them on our own machines.' },
]
const audience = ref(0)
const current = computed(() => audiences[audience.value] ?? audiences[0]!)
useSiteTheme()
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>

<header class="hero"><div class="wrap">
  <div>
    <p class="eyebrow">Open-source app platform · self-hosted</p>
    <h1>The home for every app your AI builds. <em>On servers you own.</em></h1>
    <p class="lede">Your AI can put one app on a server. Automaton runs all of them: sign-in, sharing, an app store, a catalog for your team, secrets, backups and safe updates. Your data stays where you decide, and the cost is the server you rent.</p>
    <div class="row"><RouterLink class="btn primary" to="/get-started">Let your AI set it up →</RouterLink><a class="btn" :href="publicAppConfig.storeUrl">Browse live apps</a></div>
    <div class="trust"><span>Apache 2.0</span><span>No Automaton account</span><span>One server to start</span></div>
  </div>
  <svg class="dia" viewBox="0 0 560 440" role="img" aria-labelledby="eco-t">
    <title id="eco-t">Your AI agent deploys apps to your Automaton server, which shares them privately with your team, publishes them in your app store and installs apps from catalogs.</title>
    <path class="wire" d="M150 220H210M350 220H410M280 150V90M280 290V350M350 180L420 110M350 260L420 330" />
    <rect class="hub" x="210" y="150" width="140" height="140" rx="22" />
    <text class="hubt" x="236" y="208">Automaton</text><text class="hubs" x="236" y="230">your server</text>
    <circle class="pulse" cx="324" cy="176" r="6" />
    <rect class="box" x="10" y="180" width="140" height="80" rx="14" /><text class="t" x="26" y="214">Your AI agent</text><text class="s" x="26" y="236">Claude, Codex, any</text>
    <rect class="box" x="200" y="20" width="160" height="70" rx="14" /><text class="t" x="216" y="50">Studio</text><text class="s" x="216" y="70">apps · people · access</text>
    <rect class="hotb" x="410" y="70" width="140" height="80" rx="14" /><text class="t" x="426" y="104">App store</text><text class="s" x="426" y="126">store.your-domain</text>
    <rect class="box" x="410" y="180" width="140" height="80" rx="14" /><text class="t" x="426" y="214">Team only</text><text class="s" x="426" y="236">sign-in required</text>
    <rect class="box" x="410" y="290" width="140" height="80" rx="14" /><text class="t" x="426" y="324">Public link</text><text class="s" x="426" y="346">*.apps.your-domain</text>
    <rect class="box" x="200" y="350" width="160" height="70" rx="14" /><text class="t" x="216" y="380">Catalogs</text><text class="s" x="216" y="400">install from GitHub</text>
    <circle class="hot" cx="536" cy="84" r="5" />
  </svg>
</div></header>

<section class="band alt"><div class="wrap">
  <div class="head"><p class="eyebrow">Why not just a server?</p><h2>Deploying one app is easy. Running fifty, for real people, is the work.</h2><p>Ask an AI to copy an app to a VPS and you get one app. Automaton gives you the platform around it, so the tenth app is as easy as the first and every app has the same sharing, security and recovery.</p></div>
  <div class="cmp-scroll"><table class="cmp">
    <thead><tr><th></th><th>AI + a bare server</th><th class="us">Automaton</th></tr></thead>
    <tbody>
      <tr><td>Address and HTTPS</td><td class="raw">DNS, ports, proxy and certificates wired by hand for each app</td><td class="us">Every app gets its own HTTPS address automatically</td></tr>
      <tr><td>Who can open it</td><td class="raw">Public, or a password you build yourself</td><td class="us">Per app: private, signed-in members, or public</td></tr>
      <tr><td>Sharing and discovery</td><td class="raw">Send a link and hope</td><td class="us">Your own app store, plus catalogs your team installs from in one click</td></tr>
      <tr><td>Secrets</td><td class="raw">.env files copied around</td><td class="us">A vault per app and project; platform access needs an administrator's approval</td></tr>
      <tr><td>Updates</td><td class="raw">A bad deploy takes the app down</td><td class="us">The new version goes live only after its health check passes</td></tr>
      <tr><td>Backups</td><td class="raw">If someone remembers</td><td class="us">Nightly backups; restore to a clean server tested</td></tr>
      <tr><td>Starting point</td><td class="raw">Your AI writes every app from scratch, every time</td><td class="us">Install proven apps from catalogs in one click, then fork and adapt</td></tr>
      <tr><td>Your AI's instructions</td><td class="raw">Re-explained every session</td><td class="us">Each app ships with a contract and guide your agent reads</td></tr>
    </tbody>
  </table></div>
</div></section>

<section class="band"><div class="wrap">
  <div class="head"><p class="eyebrow">One platform</p><h2>Everything an app needs after it is built</h2></div>
  <div class="grid">
    <div class="cap"><svg viewBox="0 0 36 36"><rect class="l" x="5" y="7" width="26" height="20" rx="4"/><path class="l" d="M11 15l4 3-4 3M18 22h7"/><circle class="d" cx="30" cy="8" r="4"/></svg><h3>Built for AI agents</h3><p>A command line your agent drives, with generated guidance: how to build, deploy and prove an app works.</p></div>
    <div class="cap"><svg viewBox="0 0 36 36"><circle class="l" cx="13" cy="13" r="5"/><circle class="l" cx="25" cy="15" r="4"/><path class="l" d="M4 30c1-6 5-9 9-9s8 3 9 9M20 30c1-4 3-7 6-7s5 2 6 6"/><circle class="d" cx="25" cy="15" r="2"/></svg><h3>People and access</h3><p>Invite people, choose who can open each app, and approve what apps may do.</p></div>
    <div class="cap"><svg viewBox="0 0 36 36"><path class="l" d="M6 12h24l-2 18H8z"/><path class="l" d="M12 12a6 6 0 0 1 12 0"/><circle class="d" cx="18" cy="21" r="3"/></svg><h3>Your app store</h3><p>Publish apps with a cover and a description at store.your-domain, for your team or the world.</p></div>
    <div class="cap"><svg viewBox="0 0 36 36"><rect class="l" x="5" y="5" width="11" height="11" rx="3"/><rect class="l" x="20" y="5" width="11" height="11" rx="3"/><rect class="l" x="5" y="20" width="11" height="11" rx="3"/><rect class="l" x="20" y="20" width="11" height="11" rx="3"/><circle class="d" cx="25.5" cy="25.5" r="3"/></svg><h3>Catalogs</h3><p>Point Studio at a GitHub repository of apps. Your team installs from it in one click.</p></div>
    <div class="cap"><svg viewBox="0 0 36 36"><path class="l" d="M10 6v10a8 8 0 0 0 16 0M26 6v4M18 24v6"/><circle class="l" cx="10" cy="6" r="2"/><circle class="d" cx="26" cy="6" r="3"/></svg><h3>Fork and iterate</h3><p>Copy an app to try an idea, deploy again in seconds, and keep the version that works.</p></div>
    <div class="cap"><svg viewBox="0 0 36 36"><path class="l" d="M18 4l12 5v8c0 8-5 13-12 15C11 30 6 25 6 17V9z"/><path class="l" d="M13 18l4 4 7-8"/><circle class="d" cx="30" cy="9" r="3"/></svg><h3>Data you control</h3><p>Apps, data and backups stay on your machines. Add more servers when you need them.</p></div>
  </div>
  <p style="margin-top:24px"><RouterLink class="btn" to="/product">See the whole product →</RouterLink></p>
</div></section>

<section class="band alt"><div class="wrap">
  <div class="feat" style="padding:0">
    <div><p class="eyebrow">Catalogs</p><h3>Don't rebuild what already exists</h3><p class="soft">Apps can wait as source code in GitHub repositories until you need them. Add a repository as a catalog, and its apps appear in Studio, ready to install on your server whenever you want. Pick tested, popular open-source apps instead of asking your AI to write everything from nothing.</p>
      <ul><li>Source stays in GitHub until you install it; nothing runs until you choose</li><li>Built from source on your own server, so you can read exactly what runs</li><li>Fork an installed app and let your AI adapt it</li><li>Packaging an existing open-source app takes one small file your agent can write</li></ul><p style="margin-top:18px"><RouterLink class="btn" to="/catalog">Browse the catalog →</RouterLink></p></div>
    <div style="display:grid;gap:14px;min-width:0">
      <div class="repo" role="img" aria-label="A GitHub repository of Automaton apps; each app folder contains an automaton.json file.">
        <div class="repo-h"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 0a8 8 0 0 0-2.53 15.59c.4.07.55-.17.55-.38v-1.34c-2.23.48-2.7-1.07-2.7-1.07-.36-.92-.89-1.17-.89-1.17-.73-.5.05-.49.05-.49.8.06 1.23.83 1.23.83.72 1.22 1.88.87 2.34.67.07-.52.28-.87.5-1.07-1.78-.2-3.65-.89-3.65-3.96 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.6 7.6 0 0 1 4 0c1.53-1.03 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.28.82 2.15 0 3.08-1.87 3.76-3.66 3.96.29.25.54.73.54 1.48v2.2c0 .21.15.46.55.38A8 8 0 0 0 8 0z"/></svg><b>rzvdaniel / automaton-apps</b><span class="vis">Public</span><span class="lic">Apache-2.0</span></div>
        <ul class="tree">
          <li><i class="fo"></i><span>games/</span><span class="msg">Untranslatable, Space Invaders</span></li>
          <li><i class="fo"></i><span>maintenance/</span><span class="msg">backup, links, flow check</span></li>
          <li class="ind"><i class="fo"></i><span>site-links/</span><span class="msg"></span></li>
          <li class="ind"><i class="fi"></i><span>main.py</span><span class="msg">the app, unchanged</span></li>
          <li class="ind hl"><i class="fi"></i><span>automaton.json</span><span class="msg">the only extra file</span></li>
          <li><i class="fi"></i><span>catalog.json</span><span class="msg">generated on push</span></li>
        </ul>
      </div>
      <pre class="code" aria-label="Example automaton.json"><span class="c">// maintenance/site-links/automaton.json</span>
{
  <span class="k">"name"</span>: <span class="v">"Automaton Site Links"</span>,
  <span class="k">"summary"</span>: <span class="v">"Crawls your sites and reports broken links."</span>,
  <span class="k">"application"</span>: { <span class="k">"secrets"</span>: [ <span class="v">"GITHUB_TOKEN"</span> ] }
}</pre>
    </div>
  </div>
</div></section>

<section class="band"><div class="wrap">
  <div class="head"><p class="eyebrow">Who it is for</p><h2>From one curious builder to a whole company</h2></div>
  <div class="tabs" role="tablist" aria-label="Audience">
    <button v-for="(a, i) in audiences" :key="a.h" class="tab" role="tab" type="button" :aria-selected="i === audience" @click="audience = i">{{ a.h }}</button>
  </div>
  <div class="aud" role="tabpanel" aria-live="polite">
    <div><h3>{{ current.h }}</h3><p class="soft">{{ current.p }}</p><ul><li v-for="l in current.l" :key="l">{{ l }}</li></ul></div>
    <div class="quote">{{ current.q }}<small>Example use, not a customer quote</small></div>
  </div>
</div></section>

<section class="band alt"><div class="wrap">
  <div class="head"><p class="eyebrow">Setup</p><h2>Your AI installs it. You just say where.</h2><p>Rent a small Linux server, point a domain at it, and hand your agent the address. It follows the public guide and reports back. Measured installs took 90 to 118 seconds.</p></div>
  <div class="setup">
    <div class="step"><h3>Rent a server and a domain</h3><p>Any x86_64 Ubuntu or Debian machine with 4 GB of memory. About €7 a month at common providers.</p></div>
    <div class="step"><h3>Ask your agent</h3><p>Give it SSH access and one sentence. It reads automaton.monster/llms.txt and does the rest.</p><div class="chat"><b>You</b>Install Automaton on my server at example.com and make me the administrator.</div></div>
    <div class="step"><h3>Start building</h3><p>Open Studio, invite your people, and ask for your first app. It shows up with its own address.</p><div class="chat"><b>You</b>Build a lunch poll for my team and share it with them.</div></div>
  </div>
  <p style="margin-top:24px"><RouterLink class="btn" to="/how-setup-works">How setup works, step by step →</RouterLink></p>
</div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
