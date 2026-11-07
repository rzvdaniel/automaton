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
    <p class="eyebrow">How setup works</p>
    <h1>From a rented server to your first app. <em>Mostly done by your AI.</em></h1>
    <p class="lede">You do the parts that need you: rent the server, point the domain, and say what you want. Your agent follows the public guide for everything else and tells you what it did.</p>
    <div class="row"><RouterLink class="btn primary" to="/get-started">Get started</RouterLink></div>
  </div>
  <div class="mock">
    <h3 style="font-size:1.1rem;margin-bottom:10px">What you need</h3>
    <ul class="reqs">
      <li><span>Server</span><span>x86_64, Ubuntu or Debian 12</span></li>
      <li><span>Memory</span><span>4 GB recommended</span></li>
      <li><span>Free disk</span><span>10 GiB or more</span></li>
      <li><span>Domain</span><span>api, studio, store, apps, *.apps</span></li>
      <li><span>Open ports</span><span>80, 443, 5671</span></li>
      <li><span>Included</span><span>Docker, PostgreSQL, Traefik</span></li>
    </ul>
  </div>
</div></header>
<section class="band alt"><div class="wrap">
  <div class="head"><p class="eyebrow">The steps</p><h2>Who does what, and how long it took</h2><p>Times are from fresh-server test runs on 2 vCPU, 4 GB machines in November 2026.</p></div>
  <ol class="tl">
    <li><span class="when">10 min</span><span class="dot">1</span><div><h3>Rent a server <span class="who">you</span></h3><p>Any provider. About €7 a month for the size we test on.</p></div></li>
    <li><span class="when">5 min</span><span class="dot">2</span><div><h3>Point your domain <span class="who">you</span></h3><p>Add DNS records for api, studio, store and apps. Your agent can tell you exactly which.</p></div></li>
    <li class="who-ai"><span class="when">90–118 s</span><span class="dot">3</span><div><h3>Install Automaton <span class="who">your AI</span></h3><p>It checks DNS, installs Docker if missing, downloads a verified release and starts the stack.</p></div></li>
    <li class="who-ai"><span class="when">1 min</span><span class="dot">4</span><div><h3>Create the administrator <span class="who">your AI</span></h3><p>You get a one-time link to set your password in Studio.</p></div></li>
    <li class="who-ai"><span class="when">31–32 s</span><span class="dot">5</span><div><h3>Add a Runner <span class="who">your AI</span></h3><p>The Runner runs your apps. It starts on the same machine.</p></div></li>
    <li class="who-ai"><span class="when">minutes</span><span class="dot">6</span><div><h3>Build the first app <span class="who">your AI</span></h3><p>Describe it. It is built, deployed and given its own address.</p></div></li>
    <li><span class="when">later</span><span class="dot">7</span><div><h3>Invite people and keep it fresh <span class="who">you</span></h3><p>Invite your team in Studio. Ask your agent to update the server when a new release is out: it backs up, updates and checks health.</p></div></li>
  </ol>
</div></section>
<section class="band"><div class="wrap final">
  <h2>Prefer to type it yourself?</h2>
  <p>The installation guide has every command, with what each one does.</p>
  <div class="row" style="justify-content:center"><a class="btn" :href="publicAppConfig.installUrl">Installation guide</a></div>
</div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
