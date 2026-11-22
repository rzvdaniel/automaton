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
      <li><span>Domain</span><span>2 DNS records: domain and *.domain (DNS only)</span></li>
      <li><span>Email</span><span>for certificate notices</span></li>
      <li><span>Open ports</span><span>80, 443, 5671</span></li>
      <li><span>Included</span><span>Docker, PostgreSQL, Traefik</span></li>
    </ul>
  </div>
</div></header>
<section class="band alt"><div class="wrap">
  <div class="head"><p class="eyebrow">The steps</p><h2>Who does what, and how long it took</h2><p>Times are from fresh-server test runs on 2 vCPU, 4 GB machines in November 2026. Server install to working server: 2-3 minutes in total.</p></div>
  <ol class="tl">
    <li><span class="when">10 min</span><span class="dot">1</span><div><h3>Rent a server <span class="who">you</span></h3><p>Any provider. About €7 a month for the size we test on.</p></div></li>
    <li><span class="when">5 min</span><span class="dot">2</span><div><h3>Point your domain <span class="who">you</span></h3><p>Add two A records to the server's IP, DNS only (no proxy): your domain and *.your-domain. That is enough.</p></div></li>
    <li class="who-ai"><span class="when">about 94 s</span><span class="dot">3</span><div><h3>Install Automaton <span class="who">your AI</span></h3><p>You give it SSH as root, the domain and an email for certificate notices. It checks DNS, installs Docker, downloads a verified release and starts the stack.</p></div></li>
    <li class="who-ai"><span class="when">under 1 min</span><span class="dot">4</span><div><h3>Create the administrator <span class="who">your AI</span></h3><p>It signs the CLI in with a password you never see. Last, you get a one-time link, valid once for 30 minutes: open it, choose your password and sign in to Studio. If it expired, the agent makes another.</p></div></li>
    <li class="who-ai"><span class="when">about 30 s</span><span class="dot">5</span><div><h3>Add a Runner <span class="who">your AI</span></h3><p>The Runner runs your apps. It starts on the same machine.</p></div></li>
    <li class="who-ai"><span class="when">minutes</span><span class="dot">6</span><div><h3>Build the first app <span class="who">your AI</span></h3><p>Describe it. It is created, validated, deployed, published and given its own address.</p></div></li>
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
