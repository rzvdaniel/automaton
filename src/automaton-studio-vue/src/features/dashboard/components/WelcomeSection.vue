<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'

interface Props {
  hasApiKeys: boolean
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
const hasCliActivation = computed(() => props.hasApps || props.hasRunners || props.hasPublishedApp)

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
    key: 'api-key',
    done: props.hasApiKeys,
    title: 'Create API key',
    desc: 'Required to connect the CLI and authenticate local deploys',
    action: 'Create API Key',
    route: '/apikeys',
  },
  {
    key: 'cli',
    done: hasCliActivation.value,
    title: 'Install the CLI',
    desc: 'Build source locally, deploy with Automaton CLI, and operate apps in Studio',
    action: 'Install CLI',
    route: '/downloads',
  },
  {
    key: 'app',
    done: props.hasApps,
    title: 'Create your first app locally',
    desc: 'Run automaton new, build source with your AI locally, then deploy with the CLI',
    action: 'Open Apps',
    route: '/apps',
  },
  {
    key: 'runner',
    done: props.hasRunners,
    title: 'Connect a runner',
    desc: 'Required for backend app execution and Python controllers',
    action: 'Set Up Runner',
    route: '/runners',
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
          <i class="pi pi-terminal checklist-icon"></i>
          <span class="font-semibold">Get Started with Automaton CLI</span>
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

      <div v-if="!isCollapsed" class="cli-callout mt-3">
        <div class="cli-callout-copy">
          <div class="cli-callout-title">Recommended workflow</div>
          <div class="cli-callout-desc">
            1. Create an API key
            2. Install the CLI
            3. Run <code>automaton login</code> from your terminal
          </div>
        </div>
        <div class="cli-callout-actions">
          <Button label="Create API Key" size="small" @click="router.push('/apikeys')" />
          <Button label="Install CLI" size="small" severity="secondary" outlined @click="router.push('/downloads')" />
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

.cli-callout {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.125rem;
  border: 1px solid var(--p-surface-200);
  border-radius: 0.875rem;
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--p-primary-500) 8%, white), transparent 60%),
    var(--p-surface-0);
}

:global(.app-dark) .cli-callout {
  border-color: var(--p-surface-700);
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--p-primary-500) 12%, transparent), transparent 60%),
    var(--p-surface-900);
}

.cli-callout-copy {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.cli-callout-title {
  font-weight: 700;
}

.cli-callout-desc {
  color: var(--p-text-muted-color);
  font-size: 0.92rem;
  line-height: 1.45;
}

.cli-callout-actions {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
  justify-content: flex-end;
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

@media (max-width: 768px) {
  .cli-callout {
    flex-direction: column;
    align-items: flex-start;
  }

  .cli-callout-actions {
    width: 100%;
    justify-content: flex-start;
  }
}
</style>
