<script setup lang="ts">
import { computed } from 'vue'
import { useAppStore } from '@/shared/stores/app.store'

const appStore = useAppStore()

// Navigation items per design spec
const mainNavItems = [
  { title: 'Dashboard', icon: 'mdi-view-dashboard', to: '/' },
  { title: 'Flows', icon: 'mdi-flash', to: '/flows' },
  { title: 'Apps', icon: 'mdi-earth', to: '/apps' },
  { title: 'Steps', icon: 'mdi-puzzle', to: '/steps' },
  { title: 'Runners', icon: 'mdi-desktop-classic', to: '/runners' },
  { title: 'Projects', icon: 'mdi-domain', to: '/projects' },
]

const bottomNavItems = [
  { title: 'Settings', icon: 'mdi-cog', to: '/settings' },
  { title: 'Docs', icon: 'mdi-book-open-variant', href: 'https://docs.automaton.io' },
]

// Rail width: 72px collapsed, 256px expanded
const railWidth = computed(() => appStore.rail ? 72 : 256)
</script>

<template>
  <v-navigation-drawer
    v-model="appStore.drawer"
    :rail="appStore.rail"
    :width="railWidth"
    permanent
    class="border-e"
  >
    <!-- Logo Header -->
    <v-list-item class="py-4 px-2" :class="{ 'justify-center': appStore.rail }">
      <template v-if="appStore.rail">
        <v-avatar color="primary" size="40">
          <v-icon icon="mdi-robot" size="24" />
        </v-avatar>
      </template>
      <template v-else>
        <div class="d-flex align-center">
          <v-avatar color="primary" size="40" class="mr-3">
            <v-icon icon="mdi-robot" size="24" />
          </v-avatar>
          <div>
            <div class="text-h6 font-weight-bold">Automaton</div>
            <div class="text-caption text-medium-emphasis">Studio</div>
          </div>
        </div>
      </template>
    </v-list-item>

    <v-divider />

    <!-- Main Navigation Items -->
    <v-list density="compact" nav class="px-2 mt-2">
      <v-list-item
        v-for="item in mainNavItems"
        :key="item.title"
        :to="item.to"
        :prepend-icon="item.icon"
        :title="appStore.rail ? undefined : item.title"
        color="primary"
        rounded="lg"
        class="mb-1"
      >
        <v-tooltip v-if="appStore.rail" activator="parent" location="end">
          {{ item.title }}
        </v-tooltip>
      </v-list-item>
    </v-list>

    <!-- Bottom Section -->
    <template #append>
      <v-divider />
      <v-list density="compact" nav class="px-2 py-2">
        <v-list-item
          v-for="item in bottomNavItems"
          :key="item.title"
          :to="item.to"
          :href="item.href"
          :target="item.href ? '_blank' : undefined"
          :prepend-icon="item.icon"
          :title="appStore.rail ? undefined : item.title"
          rounded="lg"
          class="mb-1"
        >
          <v-tooltip v-if="appStore.rail" activator="parent" location="end">
            {{ item.title }}
          </v-tooltip>
        </v-list-item>

        <!-- Collapse/Expand Toggle -->
        <v-list-item
          :prepend-icon="appStore.rail ? 'mdi-chevron-right' : 'mdi-chevron-left'"
          :title="appStore.rail ? undefined : 'Collapse'"
          rounded="lg"
          @click="appStore.toggleRail"
        >
          <v-tooltip v-if="appStore.rail" activator="parent" location="end">
            Expand
          </v-tooltip>
        </v-list-item>
      </v-list>
    </template>
  </v-navigation-drawer>
</template>
