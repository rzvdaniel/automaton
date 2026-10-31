<script setup lang="ts">
import { onMounted } from 'vue'
import { RouterView } from 'vue-router'
import StoreHeader from '@/components/StoreHeader.vue'
import StoreFooter from '@/components/StoreFooter.vue'
import { useSiteTheme } from '@/composables/useSiteTheme'
import { useStoreStatus } from '@/composables/useStoreStatus'

const { checked, enabled, load } = useStoreStatus()

useSiteTheme()
onMounted(load)
</script>

<template>
  <RouterView v-if="enabled" />
  <div v-else-if="checked" class="site">
    <StoreHeader />
    <main class="band">
      <div class="wrap disabled-store">
        <h1>This app store is turned off</h1>
        <p>The administrator of this server has not made its apps public.</p>
      </div>
    </main>
    <StoreFooter />
  </div>
</template>

<style scoped>
.disabled-store {
  padding-block: 96px;
  max-width: 40rem;
}
</style>
