<script setup lang="ts">
import Card from 'primevue/card'
import Button from 'primevue/button'
</script>

<template>
  <div class="home-view">
    <div class="home-header">
      <h1>Welcome to Automaton Studio</h1>
      <p class="text-color-secondary">AI-powered workflow automation platform</p>
    </div>

    <div class="grid">
      <div class="col-12 md:col-6 lg:col-3">
        <Card>
          <template #title>
            <div class="flex items-center gap-2">
              <i class="pi pi-bolt text-xl" style="color: var(--p-primary-500)"></i>
              <span>Active Flows</span>
            </div>
          </template>
          <template #content>
            <div class="text-3xl font-bold">12</div>
            <div class="text-color-secondary text-sm">+2 new this week</div>
          </template>
        </Card>
      </div>

      <div class="col-12 md:col-6 lg:col-3">
        <Card>
          <template #title>
            <div class="flex items-center gap-2">
              <i class="pi pi-play text-xl" style="color: var(--p-green-500)"></i>
              <span>Executions</span>
            </div>
          </template>
          <template #content>
            <div class="text-3xl font-bold">1,234</div>
            <div class="text-color-secondary text-sm">Last 24 hours</div>
          </template>
        </Card>
      </div>

      <div class="col-12 md:col-6 lg:col-3">
        <Card>
          <template #title>
            <div class="flex items-center gap-2">
              <i class="pi pi-check-circle text-xl" style="color: var(--p-teal-500)"></i>
              <span>Success Rate</span>
            </div>
          </template>
          <template #content>
            <div class="text-3xl font-bold">94.2%</div>
            <div class="text-color-secondary text-sm">+1.2% from last week</div>
          </template>
        </Card>
      </div>

      <div class="col-12 md:col-6 lg:col-3">
        <Card>
          <template #title>
            <div class="flex items-center gap-2">
              <i class="pi pi-desktop text-xl" style="color: var(--p-blue-500)"></i>
              <span>Runners</span>
            </div>
          </template>
          <template #content>
            <div class="text-3xl font-bold">3 / 5</div>
            <div class="text-color-secondary text-sm">Online</div>
          </template>
        </Card>
      </div>
    </div>

    <div class="grid mt-4">
      <div class="col-12 lg:col-8">
        <Card>
          <template #title>Quick Actions</template>
          <template #content>
            <div class="flex flex-wrap gap-3">
              <Button label="Generate Flow with AI" icon="pi pi-sparkles" />
              <Button label="Create App" icon="pi pi-globe" severity="secondary" outlined />
              <Button label="New Step" icon="pi pi-puzzle-piece" severity="secondary" outlined />
            </div>
          </template>
        </Card>
      </div>

      <div class="col-12 lg:col-4">
        <Card>
          <template #title>Getting Started</template>
          <template #content>
            <p class="text-color-secondary">
              Describe your automation in plain English and let AI build your workflow.
            </p>
            <Button label="Learn More" link class="mt-2 p-0" />
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.home-view {
  max-width: 1400px;
  margin: 0 auto;
}

.home-header {
  margin-bottom: 2rem;
}

.home-header h1 {
  margin: 0 0 0.5rem 0;
  font-size: 1.875rem;
  font-weight: 700;
}
</style>
