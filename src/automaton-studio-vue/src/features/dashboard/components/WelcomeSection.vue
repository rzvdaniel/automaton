<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import type { App } from '@/features/apps/types/apps.types'

interface Props {
  sampleApps: App[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  dismiss: []
}>()

const router = useRouter()

// Show up to 3 sample apps
const displayedSamples = computed(() => props.sampleApps.slice(0, 3))

function navigateToCreateApp() {
  router.push('/apps')
}

function openSampleApp(app: App) {
  if (app.publicUrl) {
    window.open(`/web/${app.publicUrl}`, '_blank')
  }
}

function handleDismiss() {
  emit('dismiss')
}
</script>

<template>
  <Card class="welcome-section mb-4">
    <template #content>
      <div class="welcome-content">
        <!-- Header with dismiss button -->
        <div class="welcome-header">
          <div class="welcome-title">
            <i class="pi pi-sparkles welcome-icon"></i>
            <div>
              <h2 class="m-0">Welcome to Automaton</h2>
              <p class="text-color-secondary mt-1 mb-0">
                Build web apps with AI-powered backend logic
              </p>
            </div>
          </div>
          <Button
            icon="pi pi-times"
            text
            rounded
            severity="secondary"
            aria-label="Dismiss welcome section"
            @click="handleDismiss"
          />
        </div>

        <!-- Quick start steps -->
        <div class="quick-start mt-4">
          <h3 class="text-lg font-medium mb-3">Quick Start</h3>
          <div class="steps-row">
            <div class="step-item">
              <div class="step-number">1</div>
              <div class="step-content">
                <span class="step-title">Create App</span>
                <span class="step-desc">Start with a blank canvas or AI</span>
              </div>
            </div>
            <i class="pi pi-arrow-right step-arrow"></i>
            <div class="step-item">
              <div class="step-number">2</div>
              <div class="step-content">
                <span class="step-title">Build</span>
                <span class="step-desc">Design your UI and add backend logic</span>
              </div>
            </div>
            <i class="pi pi-arrow-right step-arrow"></i>
            <div class="step-item">
              <div class="step-number">3</div>
              <div class="step-content">
                <span class="step-title">Publish</span>
                <span class="step-desc">Share your app with a public URL</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Sample apps (if available) -->
        <div v-if="displayedSamples.length > 0" class="sample-apps mt-4">
          <h3 class="text-lg font-medium mb-3">Explore Sample Apps</h3>
          <div class="sample-apps-grid">
            <div
              v-for="app in displayedSamples"
              :key="app.id"
              class="sample-app-card"
              @click="openSampleApp(app)"
            >
              <div class="sample-app-icon">
                <i class="pi pi-globe"></i>
              </div>
              <div class="sample-app-info">
                <span class="sample-app-name">{{ app.displayName || app.name }}</span>
                <span class="sample-app-desc">{{ app.description || 'Sample application' }}</span>
              </div>
              <i class="pi pi-external-link sample-app-link"></i>
            </div>
          </div>
        </div>

        <!-- CTA -->
        <div class="welcome-cta mt-4">
          <Button
            label="Create Your First App"
            icon="pi pi-plus"
            @click="navigateToCreateApp"
          />
        </div>
      </div>
    </template>
  </Card>
</template>

<style scoped>
.welcome-section {
  border-left: 4px solid var(--p-primary-500);
}

.welcome-content {
  padding: 0.5rem;
}

.welcome-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.welcome-title {
  display: flex;
  align-items: flex-start;
  gap: 1rem;
}

.welcome-icon {
  font-size: 2rem;
  color: var(--p-primary-500);
  margin-top: 0.25rem;
}

.welcome-title h2 {
  font-size: 1.5rem;
  font-weight: 600;
  color: var(--p-text-color);
}

/* Quick start steps */
.steps-row {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.step-item {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  flex: 1;
  min-width: 180px;
}

.step-number {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: var(--p-primary-500);
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 600;
  flex-shrink: 0;
}

.step-content {
  display: flex;
  flex-direction: column;
}

.step-title {
  font-weight: 600;
  color: var(--p-text-color);
}

.step-desc {
  font-size: 0.875rem;
  color: var(--p-text-muted-color);
}

.step-arrow {
  color: var(--p-text-muted-color);
  flex-shrink: 0;
}

/* Sample apps grid */
.sample-apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
  gap: 1rem;
}

.sample-app-card {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid var(--p-surface-200);
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.sample-app-card:hover {
  border-color: var(--p-primary-500);
  transform: translateY(-2px);
}

:global(.app-dark) .sample-app-card {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .sample-app-card:hover {
  border-color: var(--p-primary-500);
}

.sample-app-icon {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  background: var(--p-primary-100);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

:global(.app-dark) .sample-app-icon {
  background: var(--p-primary-900);
}

.sample-app-icon i {
  font-size: 1.25rem;
  color: var(--p-primary-500);
}

.sample-app-info {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

.sample-app-name {
  font-weight: 600;
  color: var(--p-text-color);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.sample-app-desc {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.sample-app-link {
  color: var(--p-text-muted-color);
  flex-shrink: 0;
}

/* CTA */
.welcome-cta {
  display: flex;
  gap: 1rem;
}

/* Responsive */
@media (max-width: 768px) {
  .step-arrow {
    display: none;
  }

  .steps-row {
    flex-direction: column;
    align-items: stretch;
  }

  .step-item {
    min-width: auto;
  }
}
</style>
