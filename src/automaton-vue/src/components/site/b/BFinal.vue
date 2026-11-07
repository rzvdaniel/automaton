<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
<section class="band alt"><div class="wrap final">
  <p class="eyebrow">Open source · Apache 2.0</p>
  <h2>Own the place where your apps live.</h2>
  <p>See apps running on Automaton today, read the docs as plain text, or start your own server.</p>
  <div class="row" style="justify-content:center"><RouterLink class="btn primary" to="/get-started">Get started</RouterLink><a class="btn" :href="publicAppConfig.storeUrl">Live apps</a><RouterLink class="btn" to="/product">Product</RouterLink></div>
</div></section>
</template>
