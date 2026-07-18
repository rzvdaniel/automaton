<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import TabMenu from 'primevue/tabmenu'

const route = useRoute()
const router = useRouter()

const tabs = [
  { label: 'Overview', icon: 'pi pi-server', to: '/admin/hosted-runners' },
  { label: 'Specifications', icon: 'pi pi-sliders-h', to: '/admin/hosted-runners/specifications' },
]

const activeIndex = computed(() => route.path.endsWith('/specifications') ? 1 : 0)

function navigate(index: number) {
  const tab = tabs[index]
  if (tab) router.push(tab.to)
}
</script>

<template>
  <TabMenu :model="tabs" :active-index="activeIndex" class="mb-4" @tab-change="navigate($event.index)" />
</template>
