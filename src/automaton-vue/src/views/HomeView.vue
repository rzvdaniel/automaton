<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import MarketplaceAppRow from '@/components/site/MarketplaceAppRow.vue'
import SiteFooter from '@/components/site/SiteFooter.vue'
import SiteHeader from '@/components/site/SiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useMarketplaceApps } from '@/composables/useMarketplaceApps'
import { useSiteTheme } from '@/composables/useSiteTheme'

const MARKETPLACE_TAKE = 5

const installCommand = `curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
less install-automaton.sh
sudo bash install-automaton.sh`
const tokenCommand = 'sudo cat /etc/automaton/secrets/setup-token'
const cliCommands = `dotnet tool install -g Automaton.Cli
automaton login --server https://api.your-domain.com
automaton new "My App"
automaton deploy
automaton publish --yes`
const agentPrompt = `Build [APP IDEA] and publish it to my Automaton server at ${publicAppConfig.apiBaseUrl}.`

const toast = ref('')
let toastTimer: ReturnType<typeof setTimeout> | undefined

async function copy(text: string) {
  try {
    await navigator.clipboard.writeText(text)
    showToast('Copied')
  } catch {
    showToast('Copy failed. Select the text and copy it manually.')
  }
}

function showToast(message: string) {
  toast.value = message
  clearTimeout(toastTimer)
  toastTimer = setTimeout(() => (toast.value = ''), 1800)
}

const { apps, loading, error, loadApps } = useMarketplaceApps(MARKETPLACE_TAKE)

useSiteTheme()
onMounted(() => void loadApps(MARKETPLACE_TAKE))
onBeforeUnmount(() => clearTimeout(toastTimer))
</script>

<template>
  <div class="site home">
    <SiteHeader />

    <main>
      <section class="band hero">
        <div class="wrap">
          <div>
            <p class="eyebrow">Open source · Self-hosted · Early access</p>
            <h1>Your own place to run the apps <span class="hl">your AI builds.</span></h1>
            <p class="lede">
              Automaton is free software you install on your own server. Ask your coding agent to
              build an app, deploy it with one command, and share the link. The apps and their data
              stay on your machines.
            </p>
            <div class="cta">
              <a class="btn primary" href="#start">Install on your server <span class="arrow" aria-hidden="true">→</span></a>
              <a class="btn" :href="publicAppConfig.llmsUrl">Read it as plain text</a>
            </div>
            <ul class="facts" aria-label="At a glance">
              <li>Apache 2.0</li>
              <li>One Ubuntu server to start</li>
              <li>No cloud account needed</li>
            </ul>
          </div>

          <svg class="scene" viewBox="0 0 520 400" role="img" aria-labelledby="scene-title">
            <title id="scene-title">A small clockwork automaton carries an app from a laptop to your own server.</title>
            <path class="stroke" d="M24 340h472" />
            <rect class="stroke fill-panel" x="34" y="232" width="128" height="84" rx="8" />
            <path class="stroke" d="M22 316h152l-10 18H32z" />
            <text class="label" x="48" y="262">$ automaton</text>
            <text class="label" x="48" y="284">  deploy</text>
            <text class="tiny" x="48" y="304">your laptop</text>
            <rect class="stroke fill-paper" x="364" y="150" width="118" height="190" rx="10" />
            <path class="stroke" d="M364 212h118M364 274h118" />
            <circle class="fill-signal led" cx="458" cy="181" r="6" />
            <circle class="stroke" cx="436" cy="181" r="4" />
            <circle class="stroke" cx="458" cy="243" r="4" />
            <circle class="stroke" cx="458" cy="305" r="4" />
            <path class="stroke" d="M380 178h34M380 186h22M380 240h34M380 302h34" />
            <text class="tiny" x="376" y="132">your server</text>
            <path class="stroke" d="M178 290c60-60 120-70 176-60" stroke-dasharray="4 9" />
            <g class="walker">
              <g transform="translate(222 176)">
                <path class="stroke" d="M30 120v32M62 120v32M22 152h16M54 152h16" />
                <rect class="stroke fill-paper" x="12" y="64" width="68" height="60" rx="12" />
                <rect class="stroke fill-paper" x="6" y="0" width="80" height="62" rx="14" />
                <path class="face" d="M22 22l9 7-9 7M70 22l-9 7 9 7M38 46h16" />
                <path class="stroke" d="M46 0v-14" />
                <circle class="fill-signal" cx="46" cy="-18" r="5" />
                <g transform="translate(80 92)">
                  <path class="stroke" d="M0 0h12" />
                  <g class="key"><path class="stroke" d="M12 0c0-10 16-10 16 0s-16 10-16 0zM12 0c0 10 16 10 16 0" /></g>
                </g>
                <path class="stroke" d="M12 78c-14 6-18 18-12 26M80 78c10 4 14 10 14 18" />
                <rect class="parcel" x="-30" y="88" width="56" height="44" rx="5" />
                <path class="parcel-tape" d="M-30 104h56M-2 88v44" />
                <text class="parcel-label" x="-24" y="125">app</text>
              </g>
            </g>
          </svg>
        </div>
      </section>

      <hr class="rule" />

      <section id="start" class="band block" aria-labelledby="start-title">
        <div class="wrap">
          <div class="section-head">
            <p class="eyebrow">Get started</p>
            <h2 id="start-title">Three steps from an empty server to a live app</h2>
            <p>
              Install Automaton once, connect the machine that will run your apps, then deploy from
              your own computer.
            </p>
          </div>

          <ol class="steps">
            <li class="step">
              <h3>Install the server</h3>
              <p>
                Downloads a verified release, prepares credentials and starts Studio and the API.
                Installs Docker if it is missing.
              </p>
              <span class="where">on your server</span>
              <div class="cmd">
                <button class="copy" type="button" @click="copy(installCommand)">Copy</button>
                <pre><span class="c"># download, read, then run</span>
{{ installCommand }}</pre>
              </div>
            </li>
            <li class="step">
              <h3>Set up Studio</h3>
              <p>
                Open <code>studio.your-domain.com/install</code>, create your administrator with the
                setup token, then install a Runner from <strong>Runners</strong>. Apps need one, static
                sites included.
              </p>
              <span class="where">on your server</span>
              <div class="cmd">
                <button class="copy" type="button" @click="copy(tokenCommand)">Copy</button>
                <pre><span class="c"># read the one-time setup token</span>
{{ tokenCommand }}</pre>
              </div>
            </li>
            <li class="step">
              <h3>Build and deploy</h3>
              <p>
                Install the CLI (needs the .NET 9 SDK or later), connect it to your server, and let
                your coding agent build the app.
              </p>
              <span class="where">on your computer</span>
              <div class="cmd">
                <button class="copy" type="button" @click="copy(cliCommands)">Copy</button>
                <pre>{{ cliCommands }}</pre>
              </div>
            </li>
          </ol>
          <p class="more">
            Certificates, networking and updates are covered in the
            <a :href="publicAppConfig.installUrl">installation guide</a>.
          </p>
        </div>
      </section>

      <section class="band signal-band" aria-labelledby="needs-title">
        <div class="wrap">
          <div>
            <p class="eyebrow">Before you install</p>
            <h2 id="needs-title">What you need</h2>
            <p>
              One rented or owned server is enough to start. You pay your provider for the server
              and domain; Automaton itself is free.
            </p>
          </div>
          <ul class="req">
            <li><span>Server</span><span>x86_64, Ubuntu 24.04+</span></li>
            <li><span>Free disk space</span><span>10 GiB or more</span></li>
            <li><span>DNS records</span><span>api, studio, store, apps, *.apps</span></li>
            <li><span>Open ports</span><span>TCP 80, 443, 5671</span></li>
            <li><span>On your computer</span><span>.NET 9 SDK or later</span></li>
          </ul>
        </div>
      </section>

      <section id="why" class="band block" aria-labelledby="why-title">
        <div class="wrap">
          <div class="section-head">
            <p class="eyebrow">Why Automaton</p>
            <h2 id="why-title">A home for small apps that matter to you</h2>
            <p>
              Team tools, dashboards, games, internal APIs and scheduled jobs. Things too small for a
              cloud platform bill and too useful to leave on a laptop.
            </p>
          </div>

          <div class="reasons">
            <article class="reason">
              <svg viewBox="0 0 64 64" aria-hidden="true">
                <path class="stroke" d="M12 30L32 14l20 16v22H12z" />
                <path class="stroke" d="M26 52V38h12v14" />
                <circle class="dot" cx="32" cy="29" r="4" />
              </svg>
              <div>
                <h3>You own all of it</h3>
                <p>
                  Your server, your domain, your data. Apache 2.0 licensed, with no required
                  Automaton cloud account or subscription.
                </p>
              </div>
            </article>
            <article class="reason">
              <svg viewBox="0 0 64 64" aria-hidden="true">
                <rect class="stroke" x="10" y="14" width="44" height="34" rx="6" />
                <path class="stroke" d="M20 26l6 5-6 5M32 38h10" />
                <circle class="dot" cx="50" cy="14" r="5" />
              </svg>
              <div>
                <h3>Made for coding agents</h3>
                <p>
                  <code>automaton new</code> writes guidance your agent reads: the app contract, the
                  deploy steps and how to prove the app works. Use Claude Code, Codex or any agent
                  you like.
                </p>
              </div>
            </article>
            <article class="reason">
              <svg viewBox="0 0 64 64" aria-hidden="true">
                <rect class="stroke" x="8" y="12" width="22" height="18" rx="4" />
                <rect class="stroke" x="34" y="12" width="22" height="18" rx="4" />
                <rect class="stroke" x="8" y="34" width="22" height="18" rx="4" />
                <rect class="stroke" x="34" y="34" width="22" height="18" rx="4" />
                <circle class="dot" cx="45" cy="43" r="4" />
              </svg>
              <div>
                <h3>Real apps, not only pages</h3>
                <p>
                  Static sites, server apps and APIs, and Actions for jobs. App data persists across
                  redeploys, on the machine you choose.
                </p>
              </div>
            </article>
            <article class="reason">
              <svg viewBox="0 0 64 64" aria-hidden="true">
                <path class="stroke" d="M32 10a22 22 0 1 0 22 22" />
                <path class="stroke" d="M54 14v14H40" />
                <path class="stroke" d="M32 20v12l8 6" />
                <circle class="dot" cx="32" cy="32" r="3.5" />
              </svg>
              <div>
                <h3>Calm deploys</h3>
                <p>
                  A new version goes live only after its health check passes; otherwise the previous
                  one keeps serving. Old containers and files are cleaned up for you.
                </p>
              </div>
            </article>
          </div>
        </div>
      </section>

      <hr class="rule" />

      <!-- id="architecture" keeps the shared header's "How it works" link working. -->
      <section id="architecture" class="band block" aria-labelledby="how-title">
        <div class="wrap">
          <div class="section-head">
            <p class="eyebrow">How it fits together</p>
            <h2 id="how-title">What runs where</h2>
            <p>
              Your computer builds and deploys. Your server runs Studio and the API. A Runner runs
              each app in its own container.
            </p>
          </div>
          <div class="flow">
            <svg viewBox="0 0 1000 220" role="img" aria-labelledby="flow-title">
              <title id="flow-title">
                Your computer deploys to Studio and the API on your server, which hands the app to a
                Runner that serves it at your app URL.
              </title>
              <defs>
                <marker id="flow-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="8" markerHeight="8" orient="auto">
                  <path class="head" d="M0 0L10 5L0 10z" />
                </marker>
              </defs>
              <rect class="box" x="10" y="40" width="210" height="140" rx="18" />
              <text class="t" x="32" y="78">Your computer</text>
              <text class="s" x="32" y="104">Your code and coding agent</text>
              <text class="m" x="32" y="150">automaton deploy</text>
              <path class="wire" d="M224 110h48" marker-end="url(#flow-arrow)" />
              <rect class="box" x="280" y="40" width="210" height="140" rx="18" />
              <text class="t" x="302" y="78">Studio + API</text>
              <text class="s" x="302" y="104">Users, apps and access</text>
              <text class="m" x="302" y="150">studio.your-domain</text>
              <path class="wire" d="M494 110h48" marker-end="url(#flow-arrow)" />
              <rect class="box-signal" x="550" y="40" width="210" height="140" rx="18" />
              <text class="t-on" x="572" y="78">Runner</text>
              <text class="s-on" x="572" y="104">Each app in a container</text>
              <text class="s-on" x="572" y="150">same or another machine</text>
              <path class="wire" d="M764 110h48" marker-end="url(#flow-arrow)" />
              <rect class="box" x="820" y="40" width="170" height="140" rx="18" />
              <text class="t" x="842" y="78">Your app</text>
              <text class="s" x="842" y="104">Live over HTTPS</text>
              <text class="m" x="842" y="150">app.apps.your…</text>
            </svg>
          </div>
        </div>
      </section>

      <section id="apps" class="band block" aria-labelledby="apps-title">
        <div class="wrap">
          <div class="marketplace">
            <div class="section-head">
              <p class="eyebrow">Public apps</p>
              <h2 id="apps-title">Apps people run on Automaton</h2>
              <p>
                Apps published to the public marketplace from Automaton installations. Open one, or
                publish your own from Studio when it is ready to share.
              </p>
              <div class="cta">
                <RouterLink class="btn small" to="/apps">Browse all public apps <span class="arrow" aria-hidden="true">→</span></RouterLink>
              </div>
            </div>

            <div class="app-list" :aria-busy="loading">
              <p v-if="loading && apps.length === 0" class="app-state">Loading public apps…</p>
              <p v-else-if="error" class="app-state">
                Public apps could not be loaded right now. <RouterLink to="/apps">Try the apps page</RouterLink>.
              </p>
              <p v-else-if="apps.length === 0" class="app-state">
                No public apps yet. Publish one from Studio and it appears here.
              </p>
              <ul v-else>
                <li v-for="app in apps" :key="app.id"><MarketplaceAppRow :app="app" /></li>
              </ul>
            </div>
          </div>
        </div>
      </section>

      <section class="band block" aria-labelledby="limits-title">
        <div class="wrap">
          <div class="section-head">
            <p class="eyebrow">Early access</p>
            <h2 id="limits-title">Good to know before you start</h2>
          </div>
          <div class="limits">
            <div class="limit">
              <h3>You run the server</h3>
              <p>
                Updates, backups and the machine itself are yours. The updater makes a local recovery
                backup, and services can restart briefly during an update.
              </p>
            </div>
            <div class="limit">
              <h3>No live connections yet</h3>
              <p>
                Apps cannot use WebSockets or streamed responses. For live views, poll a small JSON
                endpoint, for example once a second.
              </p>
            </div>
            <div class="limit">
              <h3>Tested on one server</h3>
              <p>
                The full app lifecycle runs on a production server today. Multi-machine and recovery
                testing are still limited.
              </p>
            </div>
          </div>
        </div>
      </section>

      <hr class="rule" />

      <section id="agents" class="band block" aria-labelledby="agents-title">
        <div class="wrap">
          <div class="agents">
            <div class="section-head">
              <p class="eyebrow">For coding agents</p>
              <h2 id="agents-title">Point your agent here</h2>
              <p>
                Everything on this page is also published as plain text at
                <a :href="publicAppConfig.llmsUrl">llms.txt</a>. Paste this prompt into your agent to
                build your first app.
              </p>
              <dl class="facts-table">
                <dt>what</dt>
                <dd>Self-hosted platform for web apps, APIs and Actions</dd>
                <dt>license</dt>
                <dd>Apache 2.0</dd>
                <dt>cli</dt>
                <dd><code>dotnet tool install -g Automaton.Cli</code></dd>
                <dt>contract</dt>
                <dd><code>automaton.json</code> in the app folder</dd>
                <dt>port</dt>
                <dd>listen on <code>AUTOMATON_PORT</code>, bind <code>0.0.0.0</code></dd>
                <dt>data</dt>
                <dd>write durable files under <code>AUTOMATON_APP_DATA_DIR</code></dd>
              </dl>
            </div>
            <div class="cmd">
              <button class="copy" type="button" @click="copy(agentPrompt)">Copy</button>
              <pre>{{ agentPrompt }}</pre>
            </div>
          </div>
        </div>
      </section>
    </main>

    <SiteFooter />

    <div class="toast" :class="{ show: toast }" role="status" aria-live="polite">{{ toast }}</div>
  </div>
</template>

<style scoped>
/* Shared tokens, type, buttons, header and footer come from assets/site.css and components/site. */

/* Hero */
.hero .wrap {
  display: grid;
  grid-template-columns: minmax(0, 1.1fr) minmax(0, 0.9fr);
  gap: 48px;
  align-items: center;
  padding-block: 48px 72px;
}
.hero h1 {
  font-size: clamp(2.6rem, 6vw, 4.6rem);
  font-weight: 800;
  margin-block: 18px 22px;
}
.hero h1 .hl {
  color: var(--signal-ink);
}
.facts {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 20px;
  margin-top: 26px;
  font-size: 0.9rem;
  color: var(--ink-soft);
  list-style: none;
}
.facts li {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.facts li::before {
  content: '';
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--signal);
}

.scene {
  width: 100%;
  height: auto;
  max-width: 100%;
}
.scene .stroke {
  fill: none;
  stroke: var(--ink);
  stroke-width: 2.4;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.scene .fill-paper {
  fill: var(--paper);
}
.scene .fill-panel {
  fill: var(--panel);
}
.scene .fill-signal {
  fill: var(--signal);
}
.scene .face {
  fill: none;
  stroke: var(--signal);
  stroke-width: 3.2;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.scene .parcel {
  fill: var(--signal);
  stroke: var(--ink);
  stroke-width: 2.4;
}
.scene .parcel-tape {
  fill: none;
  stroke: var(--ink);
  stroke-width: 2;
}
.scene .parcel-label {
  font: 600 11px var(--mono);
  fill: var(--on-signal);
}
.scene .label {
  font: 600 13px var(--mono);
  fill: var(--ink);
}
.scene .tiny {
  font: 500 11px var(--mono);
  fill: var(--ink-soft);
}
.scene .key {
  transform-box: fill-box;
  transform-origin: 0% 50%;
  animation: wind 3.2s ease-in-out infinite;
}
.scene .walker {
  animation: bob 1.6s ease-in-out infinite;
}
.scene .led {
  animation: blink 2.4s steps(1) infinite;
}
@keyframes wind {
  0%,
  100% {
    transform: scaleX(1);
  }
  50% {
    transform: scaleX(-1);
  }
}
@keyframes bob {
  50% {
    transform: translateY(-4px);
  }
}
@keyframes blink {
  50% {
    opacity: 0.25;
  }
}

/* Sections */
.block .wrap {
  padding-block: 72px;
  display: grid;
  gap: 36px;
}
.rule {
  border: 0;
  border-top: 1px solid var(--line);
}
.more {
  color: var(--ink-soft);
}

/* Steps: a real sequence, so they are numbered. */
.steps {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 20px;
  counter-reset: step;
  list-style: none;
}
.step {
  display: grid;
  grid-template-rows: auto auto 1fr auto;
  gap: 12px;
  min-width: 0;
  padding: 26px;
  border: 1.5px solid var(--ink);
  border-radius: 22px;
}
.step::before {
  counter-increment: step;
  content: counter(step);
  font: 800 2.6rem/1 var(--display);
  color: var(--signal);
}
.step h3 {
  font-size: 1.35rem;
}
.step p {
  color: var(--ink-soft);
}
.where {
  font: 500 0.8rem var(--mono);
  color: var(--ink-soft);
}

.cmd {
  position: relative;
  min-width: 0;
  background: var(--code-bg);
  color: var(--code-fg);
  border-radius: 14px;
  padding: 16px 16px 16px 18px;
  font-size: 0.85rem;
  line-height: 1.7;
}
.cmd pre {
  margin-top: 28px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.cmd .c {
  color: var(--code-dim);
}
.copy {
  position: absolute;
  top: 10px;
  right: 10px;
  font: 600 0.75rem var(--body);
  padding: 6px 10px;
  border-radius: 999px;
  border: 1px solid var(--code-dim);
  background: transparent;
  color: var(--code-fg);
  cursor: pointer;
}
.copy:hover {
  border-color: var(--signal);
  color: var(--signal);
}

/* Signature band: the one place the page commits to the signal colour. */
.signal-band {
  background: var(--signal);
  color: var(--on-signal);
}
.signal-band .wrap {
  padding-block: 64px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 40px;
  align-items: start;
}
.signal-band h2 {
  font-size: clamp(2rem, 4vw, 3rem);
  font-weight: 800;
  margin-top: 12px;
}
.signal-band p {
  font-size: 1.1rem;
  max-width: 34rem;
  margin-top: 14px;
}
.site .signal-band .eyebrow {
  color: var(--on-signal);
  opacity: 0.75;
}
.req {
  list-style: none;
  display: grid;
  border-top: 1.5px solid var(--on-signal);
}
.req li {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  padding: 12px 0;
  border-bottom: 1.5px solid var(--on-signal);
  font-weight: 500;
}
.req li span:last-child {
  font-family: var(--mono);
  font-size: 0.9rem;
  text-align: right;
}

/* Reasons */
.reasons {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 44px 56px;
}
.reason {
  display: grid;
  grid-template-columns: 64px minmax(0, 1fr);
  gap: 20px;
  align-items: start;
}
.reason svg {
  width: 64px;
  height: 64px;
}
.reason .stroke {
  fill: none;
  stroke: var(--ink);
  stroke-width: 2.2;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.reason .dot {
  fill: var(--signal);
}
.reason h3 {
  font-size: 1.3rem;
  margin-bottom: 8px;
}
.reason p {
  color: var(--ink-soft);
}

/* Architecture */
.flow {
  overflow-x: auto;
}
.flow svg {
  min-width: 760px;
  width: 100%;
  height: auto;
}
.flow .box {
  fill: var(--paper);
  stroke: var(--ink);
  stroke-width: 1.8;
}
.flow .box-signal {
  fill: var(--signal);
  stroke: var(--ink);
  stroke-width: 1.8;
}
.flow .t {
  font: 700 15px var(--display);
  fill: var(--ink);
}
.flow .t-on {
  font: 700 15px var(--display);
  fill: var(--on-signal);
}
.flow .s {
  font: 400 12.5px var(--body);
  fill: var(--ink-soft);
}
.flow .s-on {
  font: 500 12.5px var(--body);
  fill: var(--on-signal);
}
.flow .m {
  font: 500 12px var(--mono);
  fill: var(--ink-soft);
}
.flow .wire {
  fill: none;
  stroke: var(--ink);
  stroke-width: 1.8;
  stroke-dasharray: 5 6;
}
.flow .head {
  fill: var(--ink);
}

/* Public apps */
.marketplace {
  display: grid;
  grid-template-columns: minmax(0, 0.9fr) minmax(0, 1.1fr);
  gap: 40px;
  align-items: start;
  padding: 34px;
  border-radius: 26px;
  background: var(--panel);
}
.app-list ul {
  list-style: none;
  display: grid;
  gap: 10px;
}
.app-state {
  padding: 22px;
  border-radius: 18px;
  border: 1px dashed var(--line);
  color: var(--ink-soft);
  background: var(--paper);
}

/* Limits */
.limits {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  border-top: 1.5px solid var(--ink);
}
.limit {
  padding: 22px 22px 0 0;
  display: grid;
  gap: 8px;
  align-content: start;
}
.limit + .limit {
  padding-left: 22px;
  border-left: 1px solid var(--line);
}
.limit h3 {
  font-size: 1.1rem;
}
.limit p {
  color: var(--ink-soft);
  font-size: 0.97rem;
}

/* For agents */
.agents {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 40px;
  align-items: start;
}
.facts-table {
  display: grid;
  grid-template-columns: max-content minmax(0, 1fr);
  border-top: 1px solid var(--line);
  font-size: 0.95rem;
}
.facts-table dt,
.facts-table dd {
  padding: 10px 0;
  border-bottom: 1px solid var(--line);
}
.facts-table dt {
  font: 500 0.82rem var(--mono);
  color: var(--ink-soft);
  padding-right: 22px;
}

.toast {
  position: fixed;
  left: 50%;
  bottom: calc(20px + env(safe-area-inset-bottom, 0px));
  transform: translateX(-50%);
  background: var(--ink);
  color: var(--paper);
  font: 600 0.9rem var(--body);
  padding: 10px 16px;
  border-radius: 999px;
  opacity: 0;
  transition: opacity 0.2s ease;
  pointer-events: none;
}
.toast.show {
  opacity: 1;
}

@media (max-width: 900px) {
  .hero .wrap,
  .signal-band .wrap,
  .marketplace,
  .agents {
    grid-template-columns: minmax(0, 1fr);
  }
  .hero .wrap {
    padding-block: 24px 56px;
    gap: 28px;
  }
  .steps,
  .reasons,
  .limits {
    grid-template-columns: minmax(0, 1fr);
  }
  .reasons {
    gap: 32px;
  }
  .limit,
  .limit + .limit {
    padding: 18px 0;
    border-left: 0;
    border-bottom: 1px solid var(--line);
  }
}

@media (max-width: 620px) {
  .marketplace {
    padding: 20px;
  }
  .reason {
    grid-template-columns: 48px minmax(0, 1fr);
    gap: 14px;
  }
  .reason svg {
    width: 48px;
    height: 48px;
  }
}
</style>
