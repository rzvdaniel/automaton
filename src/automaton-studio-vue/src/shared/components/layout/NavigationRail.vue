<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'

const router = useRouter()
const rail = ref(true)

const navigationItems = [
  { icon: 'mdi-view-dashboard', title: 'Dashboard', path: '/' },
  { icon: 'mdi-flash', title: 'Flows', path: '/flows' },
  { icon: 'mdi-web', title: 'Apps', path: '/apps' },
  { icon: 'mdi-puzzle', title: 'Steps', path: '/steps' },
  { icon: 'mdi-server', title: 'Runners', path: '/runners' },
  { icon: 'mdi-folder-multiple', title: 'Projects', path: '/projects' }
]

const settingsItems = [
  { icon: 'mdi-cog', title: 'Settings', path: '/settings' },
  { icon: 'mdi-book-open-variant', title: 'Docs', path: '/docs' }
]

const navigateTo = (path: string) => {
  router.push(path)
}
</script>

<template>
  <v-navigation-drawer v-model:rail="rail" permanent color="surface">
    <v-list density="compact" nav>
      <v-list-item
        v-for="item in navigationItems"
        :key="item.path"
        :prepend-icon="item.icon"
        :title="item.title"
        :value="item.path"
        @click="navigateTo(item.path)"
      />
    </v-list>

    <template #append>
      <v-divider />
      <v-list density="compact" nav>
        <v-list-item
          v-for="item in settingsItems"
          :key="item.path"
          :prepend-icon="item.icon"
          :title="item.title"
          :value="item.path"
          @click="navigateTo(item.path)"
        />
      </v-list>
    </template>
  </v-navigation-drawer>
</template>
