<script setup lang="ts">
import { RouterLink } from 'vue-router'
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import BFinal from '@/components/site/b/BFinal.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useSiteTheme } from '@/composables/useSiteTheme'
import '@/assets/site-b.css'
import { ref } from 'vue'

const promptEl = ref<HTMLElement | null>(null)
const copied = ref(false)
async function copyPrompt() {
  const text = promptEl.value?.textContent ?? ''
  try {
    await navigator.clipboard.writeText(text)
    copied.value = true
    setTimeout(() => (copied.value = false), 1600)
  } catch {
    if (promptEl.value) window.getSelection()?.selectAllChildren(promptEl.value)
  }
}
useSiteTheme()
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>

<header class="hero slim"><div class="wrap">
  <p class="eyebrow">Get started</p>
  <h1>Pick how you want to begin</h1>
  <p class="lede">Most people let their AI agent do the setup. You can also follow the guide yourself, or join a server someone already runs.</p>
</div></header>
<section class="band alt" style="padding-top:48px"><div class="wrap">
  <div class="paths">
    <div class="path rec"><span class="badge">Recommended</span><h3>Let your AI do it</h3><p>Give your coding agent SSH access to a fresh server, then paste this:</p>
      <div class="prompt"><button type="button" @click="copyPrompt">{{ copied ? 'Copied' : 'Copy' }}</button><p ref="promptEl">Install Automaton on my server at [server address] for the domain [your-domain.com], following https://automaton.monster/llms.txt. Make me the administrator, add a Runner, and tell me what you did.</p></div>
      <p style="font-size:.92rem">Works with Claude Code, Codex and other agents that can use a terminal.</p></div>
    <div class="path"><h3>Do it yourself</h3><p>Follow the installation guide. One command installs the server; Studio walks you through the rest.</p>
      <ol><li>Rent a server and point your domain</li><li>Run the installer from the guide</li><li>Open the setup link it prints</li><li>Add a Runner in Studio</li></ol>
      <a class="btn" :href="publicAppConfig.installUrl">Read the installation guide</a></div>
    <div class="path"><h3>Join an existing server</h3><p>Someone in your team already runs Automaton? Ask the administrator for an invite.</p>
      <ol><li>Accept the invite and sign in to Studio</li><li>Install the CLI for your agent</li><li>Ask your agent to build your first app</li></ol>
      <RouterLink class="btn" to="/product">What you can do in Studio</RouterLink></div>
  </div>
</div></section>
<section class="band"><div class="wrap">
  <div class="head"><p class="eyebrow">Then</p><h2>Your first hour</h2></div>
  <div class="setup">
    <div class="step"><h3>Build something small</h3><div class="chat"><b>You</b>Build a page where my team can vote on Friday lunch.</div></div>
    <div class="step"><h3>Share it</h3><div class="chat"><b>You</b>Make it visible to signed-in members and invite Ana and Marc.</div></div>
    <div class="step"><h3>Publish it</h3><div class="chat"><b>You</b>Add a cover and publish it to our app store.</div></div>
  </div>
</div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
