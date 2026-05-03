<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'

interface Props {
  hasAiKeys: boolean
  hasRunners: boolean
  hasApps: boolean
  hasPublishedApp: boolean
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), { loading: false })

const emit = defineEmits<{
  dismiss: []
}>()

const router = useRouter()

const COLLAPSED_KEY = 'setup-checklist-collapsed'
const isCollapsed = ref(localStorage.getItem(COLLAPSED_KEY) === 'true')

function toggleCollapsed() {
  isCollapsed.value = !isCollapsed.value
  localStorage.setItem(COLLAPSED_KEY, String(isCollapsed.value))
}

function handleDismiss() {
  emit('dismiss')
}

const steps = computed(() => [
  {
    key: 'ai-key',
    done: props.hasAiKeys,
    title: 'Add an AI key',
    desc: 'Required to use AI generation in the App Editor',
    action: 'Add Key',
    route: '/ai-keys',
  },
  {
    key: 'runner',
    done: props.hasRunners,
    title: 'Connect a runner',
    desc: 'Executes your backend Python controllers when deploying',
    action: 'Set Up Runner',
    route: '/runners',
  },
  {
    key: 'app',
    done: props.hasApps,
    title: 'Create your first app',
    desc: 'Describe what to build — AI generates the code',
    action: 'Create App',
    route: '/apps',
  },
  {
    key: 'publish',
    done: props.hasPublishedApp,
    title: 'Publish an app',
    desc: 'Make it live with a shareable public URL',
    action: 'Go to Apps',
    route: '/apps',
  },
])

const doneCount = computed(() => steps.value.filter((s) => s.done).length)
const total = computed(() => steps.value.length)
const progressPct = computed(() => (doneCount.value / total.value) * 100)

// First incomplete step — shown as a nudge when checklist is collapsed
const nextStep = computed(() => steps.value.find((s) => !s.done))
</script>

<template>
  <Card class="setup-checklist mb-4">
    <template #content>
      <!-- Header row -->
      <div class="checklist-header">
        <div class="checklist-title-row">
          <i class="pi pi-flag checklist-icon"></i>
          <span class="font-semibold">Getting Started</span>
          <span v-if="!loading" class="checklist-progress-text">
            {{ doneCount }} of {{ total }} complete
          </span>
        </div>
        <div class="flex gap-1">
          <Button
            :icon="isCollapsed ? 'pi pi-chevron-down' : 'pi pi-chevron-up'"
            text
            rounded
            severity="secondary"
            size="small"
            :aria-label="isCollapsed ? 'Expand' : 'Collapse'"
            @click="toggleCollapsed"
          />
          <Button
            v-tooltip.top="'Hide this checklist'"
            icon="pi pi-times"
            text
            rounded
            severity="secondary"
            size="small"
            aria-label="Dismiss"
            @click="handleDismiss"
          />
        </div>
      </div>

      <!-- Progress bar -->
      <div class="progress-track mt-2">
        <div
          class="progress-fill"
          :style="{ width: `${progressPct}%` }"
          :class="{ 'progress-fill--complete': doneCount === total }"
        ></div>
      </div>

      <!-- Collapsed: show next step as a single nudge -->
      <div v-if="isCollapsed && nextStep" class="collapsed-next mt-3">
        <span class="text-color-secondary text-sm">Next: </span>
        <span class="font-medium text-sm">{{ nextStep.title }}</span>
        <Button
          :label="nextStep.action"
          size="small"
          text
          class="ml-1 p-0"
          style="vertical-align: baseline"
          @click="router.push(nextStep.route)"
        />
      </div>

      <!-- Full checklist -->
      <div v-if="!isCollapsed" class="checklist-steps mt-3">
        <div
          v-for="step in steps"
          :key="step.key"
          class="step-row"
          :class="{ 'step-row--done': step.done }"
        >
          <i
            class="step-icon"
            :class="
              step.done
                ? 'pi pi-check-circle step-icon--done'
                : 'pi pi-circle step-icon--pending'
            "
          ></i>
          <div class="step-body">
            <span class="step-title">{{ step.title }}</span>
            <span class="step-desc">{{ step.desc }}</span>
          </div>
          <Button
            v-if="!step.done"
            :label="step.action"
            size="small"
            severity="secondary"
            outlined
            class="step-action flex-shrink-0"
            @click="router.push(step.route)"
          />
          <Tag v-else value="Done" severity="success" class="flex-shrink-0" />
        </div>
      </div>
    </template>
  </Card>
</template>

<style scoped>
.setup-checklist {
  border-left: 4px solid var(--p-primary-500);
}

.checklist-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.checklist-title-row {
  display: flex;
  align-items: center;
  gap: 0.625rem;
}

.checklist-icon {
  font-size: 1.1rem;
  color: var(--p-primary-500);
}

.checklist-progress-text {
  font-size: 0.8rem;
  color: var(--p-text-muted-color);
  background: var(--p-surface-100);
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
}

:global(.app-dark) .checklist-progress-text {
  background: var(--p-surface-800);
}

.progress-track {
  height: 4px;
  background: var(--p-surface-200);
  border-radius: 999px;
  overflow: hidden;
}

:global(.app-dark) .progress-track {
  background: var(--p-surface-700);
}

.progress-fill {
  height: 100%;
  background: var(--p-primary-500);
  border-radius: 999px;
  transition: width 0.4s ease;
}

.progress-fill--complete {
  background: var(--p-green-500);
}

.checklist-steps {
  display: flex;
  flex-direction: column;
}

.step-row {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  padding: 0.625rem 0;
  border-bottom: 1px solid var(--p-surface-100);
}

.step-row:last-child {
  border-bottom: none;
}

:global(.app-dark) .step-row {
  border-bottom-color: var(--p-surface-800);
}

.step-row--done .step-title {
  text-decoration: line-through;
  color: var(--p-text-muted-color);
}

.step-row--done .step-desc {
  opacity: 0.6;
}

.step-icon {
  font-size: 1.15rem;
  flex-shrink: 0;
}

.step-icon--done {
  color: var(--p-green-500);
}

.step-icon--pending {
  color: var(--p-surface-400);
}

.step-body {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

.step-title {
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--p-text-color);
  line-height: 1.3;
}

.step-desc {
  font-size: 0.8rem;
  color: var(--p-text-muted-color);
  line-height: 1.3;
  margin-top: 0.1rem;
}

.step-action {
  min-width: 100px;
  text-align: center;
}
</style>
