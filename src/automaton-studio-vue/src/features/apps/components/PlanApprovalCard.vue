<template>
  <div class="plan-approval-card">
    <!-- Header -->
    <div class="plan-header">
      <span class="plan-badge" :class="planType">
        <i :class="planIcon"></i>
        {{ planLabel }}
      </span>
      <span class="plan-title">{{ planData.title }}</span>
    </div>

    <!-- Summary -->
    <p class="plan-summary">{{ planData.summary }}</p>

    <!-- Design plan: components + tech decisions -->
    <template v-if="planData.planType === 'design'">
      <div v-if="planData.components?.length" class="plan-section">
        <div class="section-label">Components</div>
        <ul class="plan-list">
          <li v-for="c in planData.components" :key="c.name">
            <strong>{{ c.name }}</strong>
            <span v-if="c.description || c.responsibility"> — {{ c.description || c.responsibility }}</span>
          </li>
        </ul>
      </div>
      <div v-if="planData.techDecisions?.length" class="plan-section">
        <div class="section-label">Tech Decisions</div>
        <ul class="plan-list">
          <li v-for="td in planData.techDecisions" :key="td.decision">
            <strong>{{ td.decision }}</strong>
            <span v-if="td.rationale"> — {{ td.rationale }}</span>
          </li>
        </ul>
      </div>
    </template>

    <!-- Implementation plan: stages -->
    <template v-else-if="planData.planType === 'implementation'">
      <div v-if="planData.stages?.length" class="plan-section">
        <div class="section-label">Stages</div>
        <ol class="plan-stages">
          <li v-for="(s, i) in planData.stages" :key="i" class="stage-item">
            <span class="stage-num">{{ i + 1 }}</span>
            <span class="stage-name">{{ s.name }}</span>
            <span v-if="s.description" class="stage-desc"> — {{ s.description }}</span>
            <span v-if="s.complexity || s.estimatedComplexity" class="complexity-badge" :class="(s.complexity || s.estimatedComplexity)?.toLowerCase()">
              {{ s.complexity || s.estimatedComplexity }}
            </span>
          </li>
        </ol>
      </div>
    </template>

    <!-- Stage complete: progress info -->
    <template v-else-if="planData.planType === 'stage_complete'">
      <p v-if="planData.stageSummary" class="stage-summary">{{ planData.stageSummary }}</p>
    </template>

    <!-- Actions -->
    <div class="plan-actions" v-if="planData.awaitingApproval">
      <template v-if="planData.planType === 'stage_complete'">
        <Button label="Continue to next stage" icon="pi pi-play" size="small" @click="approve('Approved. Continue to the next stage.')" />
        <Button label="Stop here" icon="pi pi-stop" severity="secondary" size="small" @click="approve('Stop here. Do not continue to the next stage.')" />
      </template>
      <template v-else>
        <Button
          label="Approve"
          icon="pi pi-check"
          size="small"
          @click="approve(planData.planType === 'design'
            ? 'Design plan approved. Proceed to the implementation plan.'
            : 'Implementation plan approved. Proceed with Stage 1.')"
        />
        <Button label="Refine" icon="pi pi-pencil" severity="secondary" size="small" @click="toggleRefine" />
      </template>
    </div>

    <!-- Refine input -->
    <div v-if="showRefine" class="refine-area">
      <InputText
        v-model="refineText"
        placeholder="Describe what to change..."
        class="refine-input"
        @keydown.enter="submitRefine"
      />
      <Button icon="pi pi-send" size="small" :disabled="!refineText.trim()" @click="submitRefine" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import type { PlanData } from '@/shared/types/ai.types'

const props = defineProps<{
  planData: PlanData
  onSend: (message: string) => void
}>()

const showRefine = ref(false)
const refineText = ref('')

const planType = computed(() => props.planData.planType)

const planLabel = computed(() => {
  switch (props.planData.planType) {
    case 'design': return 'Design Plan'
    case 'implementation': return 'Implementation Plan'
    case 'stage_complete': return `Stage ${(props.planData.stageIndex ?? 0) + 1} Complete`
    default: return 'Plan'
  }
})

const planIcon = computed(() => {
  switch (props.planData.planType) {
    case 'design': return 'pi pi-palette'
    case 'implementation': return 'pi pi-list'
    case 'stage_complete': return 'pi pi-check-circle'
    default: return 'pi pi-file'
  }
})

function approve(message: string) {
  props.onSend(message)
}

function toggleRefine() {
  showRefine.value = !showRefine.value
  if (!showRefine.value) refineText.value = ''
}

function submitRefine() {
  if (!refineText.value.trim()) return
  props.onSend(refineText.value.trim())
  refineText.value = ''
  showRefine.value = false
}
</script>

<style scoped>
.plan-approval-card {
  border: 1px solid var(--p-surface-border);
  border-radius: 8px;
  padding: 12px 14px;
  background: var(--p-surface-card);
  max-width: 480px;
  font-size: 0.875rem;
}

.plan-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
  flex-wrap: wrap;
}

.plan-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 12px;
  font-size: 0.75rem;
  font-weight: 600;
  white-space: nowrap;
}

.plan-badge.design {
  background: color-mix(in srgb, var(--p-blue-500) 15%, transparent);
  color: var(--p-blue-500);
}

.plan-badge.implementation {
  background: color-mix(in srgb, var(--p-green-500) 15%, transparent);
  color: var(--p-green-500);
}

.plan-badge.stage_complete {
  background: color-mix(in srgb, var(--p-orange-500) 15%, transparent);
  color: var(--p-orange-500);
}

.plan-title {
  font-weight: 600;
  color: var(--p-text-color);
}

.plan-summary {
  color: var(--p-text-muted-color);
  margin: 0 0 8px;
  line-height: 1.5;
}

.plan-section {
  margin-bottom: 8px;
}

.section-label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--p-text-muted-color);
  margin-bottom: 4px;
}

.plan-list,
.plan-stages {
  margin: 0;
  padding-left: 18px;
  color: var(--p-text-color);
  line-height: 1.6;
}

.plan-stages {
  list-style: none;
  padding-left: 0;
}

.stage-item {
  display: flex;
  align-items: baseline;
  gap: 6px;
  padding: 2px 0;
}

.stage-num {
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: var(--p-surface-100);
  color: var(--p-text-muted-color);
  font-size: 0.7rem;
  font-weight: 700;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.stage-name {
  font-weight: 500;
}

.stage-desc {
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
}

.complexity-badge {
  font-size: 0.65rem;
  padding: 1px 6px;
  border-radius: 8px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin-left: auto;
}

.complexity-badge.low    { background: color-mix(in srgb, var(--p-green-500)  15%, transparent); color: var(--p-green-500); }
.complexity-badge.medium { background: color-mix(in srgb, var(--p-yellow-500) 15%, transparent); color: var(--p-yellow-600); }
.complexity-badge.high   { background: color-mix(in srgb, var(--p-red-500)    15%, transparent); color: var(--p-red-500); }

.stage-summary {
  color: var(--p-text-muted-color);
  margin: 0 0 8px;
  font-style: italic;
}

.plan-actions {
  display: flex;
  gap: 6px;
  margin-top: 10px;
}

.refine-area {
  display: flex;
  gap: 6px;
  margin-top: 8px;
}

.refine-input {
  flex: 1;
  font-size: 0.8rem;
}
</style>
