<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import TabMenu from 'primevue/tabmenu'

const route = useRoute()
const router = useRouter()

const tabs = [
  { label: 'Runner Hosts', icon: 'pi pi-server', to: '/admin/hosted-runners' },
  { label: 'Infrastructure Runners', icon: 'pi pi-shield', to: '/admin/hosted-runners/infrastructure' },
  { label: 'Hosted Runners', icon: 'pi pi-box', to: '/admin/hosted-runners/containers' },
  { label: 'Specifications', icon: 'pi pi-sliders-h', to: '/admin/hosted-runners/specifications' },
]

const activeIndex = computed(() => {
  if (route.path.endsWith('/specifications')) return 3
  if (route.path.endsWith('/containers')) return 2
  if (route.path.endsWith('/infrastructure')) return 1
  return 0
})

function navigate(index: number) {
  const tab = tabs[index]
  if (tab) router.push(tab.to)
}
</script>

<template>
  <TabMenu :model="tabs" :active-index="activeIndex" class="mb-4" @tab-change="navigate($event.index)" />
</template>
