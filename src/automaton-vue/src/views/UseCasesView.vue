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

<header class="hero slim"><div class="wrap">
  <p class="eyebrow">Use cases</p>
  <h1>Small apps that matter. <em>Kept in one place.</em></h1>
  <p class="lede">Things too small for a cloud bill and too useful to leave on a laptop. Here is how people use one Automaton server, from a single builder to a company that keeps its data in-house.</p>
</div></header>
<section class="band alt" style="padding-top:48px"><div class="wrap">
  <div class="cases">
    <article class="case"><div class="ic"><svg viewBox="0 0 36 36"><circle class="l" cx="18" cy="12" r="6"/><path class="l" d="M6 32c1-7 6-11 12-11s11 4 12 11"/><circle class="d" cx="27" cy="9" r="3"/></svg></div><div><h3>Personal tools and side projects</h3><p>Every idea you build with your AI gets its own link. Keep drafts private and publish the good ones.</p><p class="ex"><b>Example</b>A recipe planner, a reading log and a small game, all on one €7 server.</p><div class="chips"><span>private apps</span><span>app store</span><span>forks</span></div></div></article>
    <article class="case"><div class="ic"><svg viewBox="0 0 36 36"><rect class="l" x="4" y="8" width="28" height="20" rx="4"/><path class="l" d="M10 16h10M10 21h6"/><circle class="d" cx="27" cy="20" r="3"/></svg></div><div><h3>Internal team tools</h3><p>Dashboards, forms and trackers your team signs in to. No more spreadsheets passed around.</p><p class="ex"><b>Example</b>A shift planner and a stock tracker, visible only to signed-in members.</p><div class="chips"><span>members</span><span>per-app access</span><span>secrets</span></div></div></article>
    <article class="case"><div class="ic"><svg viewBox="0 0 36 36"><path class="l" d="M6 12h24l-2 18H8z"/><path class="l" d="M12 12a6 6 0 0 1 12 0"/><circle class="d" cx="18" cy="21" r="3"/></svg></div><div><h3>Your company app store</h3><p>One storefront for every tool your company builds. People find what exists instead of building it twice.</p><p class="ex"><b>Example</b>Twelve internal tools with covers and descriptions at store.your-company.com.</p><div class="chips"><span>app store</span><span>catalogs</span><span>one-click install</span></div></div></article>
    <article class="case"><div class="ic"><svg viewBox="0 0 36 36"><path class="l" d="M18 4l12 5v8c0 8-5 13-12 15C11 30 6 25 6 17V9z"/><circle class="d" cx="18" cy="17" r="3"/></svg></div><div><h3>Data that must stay in-house</h3><p>Run AI-built apps without sending data to another cloud. Choose the machine and the country.</p><p class="ex"><b>Example</b>An HR request form whose data never leaves the company's own server in Germany.</p><div class="chips"><span>self-hosted</span><span>permissions</span><span>backups</span></div></div></article>
    <article class="case"><div class="ic"><svg viewBox="0 0 36 36"><circle class="l" cx="12" cy="14" r="5"/><circle class="l" cx="25" cy="14" r="5"/><path class="l" d="M4 30c1-5 4-8 8-8M32 30c-1-5-4-8-8-8"/><circle class="d" cx="18" cy="26" r="3"/></svg></div><div><h3>Sharing with clients and partners</h3><p>Invite a client or a partner company and choose exactly which apps they can open.</p><p class="ex"><b>Example</b>An agency gives each client a project status app, invited by email.</p><div class="chips"><span>invites</span><span>per-app access</span></div></div></article>
    <article class="case"><div class="ic"><svg viewBox="0 0 36 36"><circle class="l" cx="18" cy="18" r="13"/><path class="l" d="M18 10v8l6 4"/><circle class="d" cx="18" cy="18" r="2.5"/></svg></div><div><h3>Scheduled jobs and monitors</h3><p>Actions run on a schedule: reports, syncs, clean-ups and checks that alert you when something breaks.</p><p class="ex"><b>Example</b>A website monitor that checks five sites every five minutes and posts to Slack.</p><div class="chips"><span>actions</span><span>schedules</span><span>secrets</span></div></div></article>
  </div>
</div></section>
<section class="band"><div class="wrap">
  <div class="head"><p class="eyebrow">Cost</p><h2>The price is the server you rent</h2><p>The software is free under Apache 2.0. There is no per-app, per-user or per-seat fee.</p></div>
  <div class="cmp-scroll"><table class="cmp">
    <thead><tr><th></th><th>Typical app platform</th><th class="us">Automaton on your server</th></tr></thead>
    <tbody>
      <tr><td>Ten small apps</td><td class="raw">Billed per app, per seat or per request</td><td class="us">One server of about €7 a month in our tests, plus a domain</td></tr>
      <tr><td>Adding people</td><td class="raw">Often a per-seat price</td><td class="us">Invite as many as your server serves</td></tr>
      <tr><td>Where data lives</td><td class="raw">The provider's regions</td><td class="us">The machines and countries you choose</td></tr>
    </tbody>
  </table></div>
</div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
