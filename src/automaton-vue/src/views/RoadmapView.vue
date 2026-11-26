<script setup lang="ts">
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import BFinal from '@/components/site/b/BFinal.vue'
import { useSiteTheme } from '@/composables/useSiteTheme'
import { parseRoadmap } from '@/services/roadmap'
// ROADMAP.md in the repository root is the one source; the page is built from it.
import roadmapMarkdown from '../../../../ROADMAP.md?raw'
import '@/assets/site-b.css'

useSiteTheme()
const sections = parseRoadmap(roadmapMarkdown)
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>
      <header class="hero slim"><div class="wrap">
        <p class="eyebrow">Roadmap</p>
        <h1>Where Automaton is going. <em>Now, next, later.</em></h1>
        <p class="lede">A direction, not a promise: no dates, and the order can change. Every item links to its GitHub issue.</p>
      </div></header>
      <section v-for="(section, i) in sections" :key="section.title" class="band" :class="{ alt: i % 2 === 0 }"><div class="wrap">
        <div class="head"><h2>{{ section.title }}</h2></div>
        <div v-for="group in section.groups" :key="group.title">
          <h3 style="margin-top: 22px">{{ group.title }}</h3>
          <ul>
            <li v-for="item in group.items" :key="item.issue">
              {{ item.text }} <a :href="item.url">#{{ item.issue }}</a>
            </li>
          </ul>
        </div>
      </div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
