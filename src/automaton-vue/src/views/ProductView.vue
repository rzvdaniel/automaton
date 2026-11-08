<script setup lang="ts">
import { RouterLink } from 'vue-router'
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import BFinal from '@/components/site/b/BFinal.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useSiteTheme } from '@/composables/useSiteTheme'
import '@/assets/site-b.css'

useSiteTheme()
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>

<header class="hero"><div class="wrap">
  <div>
    <p class="eyebrow">Product</p>
    <h1>Five parts. <em>One place for all your apps.</em></h1>
    <p class="lede">Your agent talks to the CLI. You and your people use Studio and the app store. Runners run the apps on the machines you choose. Everything else, from certificates to backups, is handled for you.</p>
    <div class="row"><RouterLink class="btn primary" to="/get-started">Get started</RouterLink><RouterLink class="btn" to="/use-cases">See use cases</RouterLink></div>
  </div>
  <svg class="dia" viewBox="0 0 540 400" role="img" aria-labelledby="stack-t">
    <title id="stack-t">The Automaton stack: your agent and the CLI on top, then Studio and the app store, the Automaton server, Runners, and apps at the bottom.</title>
    <rect class="lay" x="20" y="16" width="500" height="58" rx="14"/><text class="t" x="40" y="42">You and your AI agent</text><text class="s" x="40" y="60">CLI · Claude Code, Codex or any agent</text>
    <rect class="lay" x="20" y="90" width="242" height="58" rx="14"/><text class="t" x="40" y="116">Studio</text><text class="s" x="40" y="134">apps · people · access</text>
    <rect class="layh" x="278" y="90" width="242" height="58" rx="14"/><text class="t" x="298" y="116">App store and catalogs</text><text class="s" x="298" y="134">publish · install</text>
    <rect class="hub" x="20" y="164" width="500" height="64" rx="14"/><text class="hubt" x="40" y="192">Automaton server</text><text class="hubs" x="40" y="212">API · sign-in · secrets · backups · updates</text>
    <circle class="pulse" cx="496" cy="196" r="6"/>
    <rect class="lay" x="20" y="244" width="242" height="58" rx="14"/><text class="t" x="40" y="270">Runner · office-1</text><text class="s" x="40" y="288">same machine</text>
    <rect class="lay" x="278" y="244" width="242" height="58" rx="14"/><text class="t" x="298" y="270">Runner · eu-box-2</text><text class="s" x="298" y="288">another machine</text>
    <g><rect class="box" x="20" y="318" width="74" height="62" rx="10"/><rect class="hotb" x="104" y="318" width="74" height="62" rx="10"/><rect class="box" x="188" y="318" width="74" height="62" rx="10"/><rect class="box" x="278" y="318" width="74" height="62" rx="10"/><rect class="box" x="362" y="318" width="74" height="62" rx="10"/><rect class="hotb" x="446" y="318" width="74" height="62" rx="10"/></g>
    <text class="s" x="30" y="354">web app</text><text class="s" x="118" y="354">game</text><text class="s" x="204" y="354">API</text><text class="s" x="290" y="354">action</text><text class="s" x="376" y="354">site</text><text class="s" x="460" y="354">tool</text>
  </svg>
</div></header>

<section class="band alt"><div class="wrap">
  <div class="feat">
    <div><p class="eyebrow">For your AI · the CLI</p><h3>The tool your agent drives</h3><p class="soft">Every app starts with a guide written for agents: how the app must start, where data lives, how to deploy and how to prove it works. Your agent reads it and gets on with the job.</p>
      <ul><li>Create, deploy, publish and roll forward</li><li>Read logs and app status</li><li>Set secrets without pasting them into chat</li><li>Update the server and Runners</li></ul></div>
    <div class="mock"><pre class="term"><span class="d"># what your agent runs, so you don't have to</span>
<span class="p">$</span> automaton deploy
<span class="d">  build · upload · start · health check</span>
<span class="g">  ✓ lunch-poll is live</span>
<span class="p">$</span> automaton logs lunch-poll --tail
<span class="p">$</span> automaton server update --wait
<span class="g">  ✓ 0.2.46 healthy</span></pre></div>
  </div>
  <div class="feat flip">
    <div><p class="eyebrow">For you · Studio</p><h3>Your control room in the browser</h3><p class="soft">See every app and who can open it. Invite people, approve what apps may access, add Runners and catalogs, and check backups.</p>
      <ul><li>Per-app access: private, members, or public</li><li>Invites only; no open sign-up</li><li>Permission requests you approve or decline</li></ul></div>
    <div class="mock"><div class="studio"><aside><b>Studio</b><span class="on">Apps</span><span>People</span><span>Runners</span><span>Catalog</span><span>Backups</span></aside>
      <div class="m"><div class="srow"><span>Lunch Poll</span><span class="pill">team</span><span class="pill live">live</span></div>
        <div class="srow"><span>Untranslatable</span><span class="pill">public</span><span class="pill live">live</span></div>
        <div class="srow"><span>Invoice API</span><span class="pill">private</span><span class="pill live">live</span></div>
        <div class="srow"><span>Nightly report</span><span class="pill">action</span><span class="pill okp">02:00</span></div></div></div></div>
  </div>
  <div class="feat">
    <div><p class="eyebrow">For everyone · App store and catalogs</p><h3>Share apps like products</h3><p class="soft">Publish an app with a cover and a description to your own store. Add catalogs from GitHub so your team can install ready-made tools in one click.</p>
      <ul><li>Your store at store.your-domain</li><li>Catalogs from any GitHub repository, public or private</li><li>Fork an app to make it your own</li></ul></div>
    <div class="mock"><div class="store" style="grid-template-columns:1fr 1fr;gap:12px">
      <div class="appc"><div class="cover" style="background:#f26a1b">Aa</div><div class="bd"><h3 style="font-size:1rem">Untranslatable</h3><p style="font-size:.85rem">Bluffing word game</p></div></div>
      <div class="appc"><div class="cover" style="background:#f8c4a0">♥</div><div class="bd"><h3 style="font-size:1rem">Website Monitor</h3><p style="font-size:.85rem">Outage alerts</p></div></div></div></div>
  </div>
  <div class="feat flip">
    <div><p class="eyebrow">Under the hood · Runners</p><h3>Each app in its own container, on the machine you pick</h3><p class="soft">A Runner is a small agent you install on a server. Start with one on the same machine; add more in other places when you need room or a different country.</p>
      <ul><li>A new version goes live only after its health check passes</li><li>App data survives redeploys</li><li>Scheduled Actions for jobs and reports</li></ul></div>
    <div class="mock"><dl class="kv"><dt>runner</dt><dd>office-1 · <span class="pill okp">online</span></dd><dt>apps</dt><dd>14 running · 2 scheduled</dd><dt>deploy</dt><dd>new version healthy → switched</dd><dt>data</dt><dd>kept across redeploys</dd><dt>install</dt><dd>31–32 s in recent tests</dd></dl></div>
  </div>
  <div class="feat">
    <div><p class="eyebrow">Operations · security and recovery</p><h3>The boring parts, done</h3><p class="soft">Certificates, secrets, backups and updates are part of the platform, not a weekend project.</p>
      <ul><li>Automatic HTTPS for every app</li><li>Secrets vault per app and project</li><li>Nightly backups; restore to a clean server tested</li><li>Server updates with one request and a health check</li></ul></div>
    <div class="mock"><dl class="kv"><dt>https</dt><dd>automatic, renewed</dd><dt>secrets</dt><dd>encrypted, never in logs</dd><dt>backups</dt><dd>nightly · 7 days kept on the server</dd><dt>restore</dt><dd>tested on a clean server</dd><dt>update</dt><dd>backup → update → health check</dd></dl></div>
  </div>
</div></section>

<section class="band"><div class="wrap">
  <div class="head"><p class="eyebrow">Good to know</p><h2>What Automaton does not do (yet)</h2><p>We would rather you know before you install.</p></div>
  <div class="grid">
    <div class="cap"><h3>No autoscaling</h3><p>Each app runs on one Runner. Add machines yourself; there is no automatic failover.</p></div>
    <div class="cap"><h3>Bounded live connections</h3><p>Server-Sent Events and WebSockets work, with a cap on how many one app keeps open and a request body limit.</p></div>
    <div class="cap"><h3>You run the server</h3><p>Updates are one request, but the machine, DNS and off-site copies of backups are yours.</p></div>
  </div>
</div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
