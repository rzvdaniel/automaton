<template>
  <div v-if="planData" class="plan-panel" :class="{ collapsed: !expanded }">
    <div class="plan-panel-header" @click="expanded = !expanded">
      <div class="plan-panel-title">
        <i class="pi pi-list"></i>
        <span>{{ planData.title }}</span>
        <span v-if="planHistory.length > 1" class="plan-version">v{{ planHistory.length }}</span>
      </div>
      <div class="plan-panel-actions">
        <Button
          v-if="showImplement"
          label="Implement Plan"
          icon="pi pi-play"
          size="small"
          class="implement-btn"
          @click.stop="$emit('implement')"
        />
        <i class="pi" :class="expanded ? 'pi-chevron-up' : 'pi-chevron-down'"></i>
      </div>
    </div>

    <div v-if="expanded" class="plan-panel-body">
      <p class="plan-summary">{{ planData.summary }}</p>

      <!-- Stages -->
      <div v-if="planData.stages?.length" class="plan-section">
        <div class="section-label">Stages</div>
        <ol class="plan-stages">
          <li v-for="(s, i) in planData.stages" :key="i" class="stage-item">
            <span class="stage-num">{{ i + 1 }}</span>
            <span class="stage-name">{{ s.name }}</span>
            <span v-if="s.description" class="stage-desc"> — {{ s.description }}</span>
            <span
              v-if="s.complexity || s.estimatedComplexity"
              class="complexity-badge"
              :class="(s.complexity || s.estimatedComplexity)?.toLowerCase()"
            >
              {{ s.complexity || s.estimatedComplexity }}
            </span>
          </li>
        </ol>
      </div>

      <!-- Collapsible details -->
      <details
        v-if="planData.components?.length || planData.techDecisions?.length"
        class="plan-details"
      >
        <summary class="plan-details-toggle">Details</summary>
        <div v-if="planData.components?.length" class="plan-section plan-section-inner">
          <div class="section-label">Components</div>
          <ul class="plan-list">
            <li v-for="c in planData.components" :key="c.name">
              <strong>{{ c.name }}</strong>
              <span v-if="c.description || c.responsibility"> — {{ c.description || c.responsibility }}</span>
            </li>
          </ul>
        </div>
        <div v-if="planData.techDecisions?.length" class="plan-section plan-section-inner">
          <div class="section-label">Tech Decisions</div>
          <ul class="plan-list">
            <li v-for="td in planData.techDecisions" :key="td.decision">
              <strong>{{ td.decision }}</strong>
              <span v-if="td.rationale"> — {{ td.rationale }}</span>
            </li>
          </ul>
        </div>
      </details>

      <!-- Previous plan versions -->
      <details v-if="previousPlans.length > 0" class="plan-details plan-history">
        <summary class="plan-details-toggle">{{ previousPlans.length }} previous {{ previousPlans.length === 1 ? 'version' : 'versions' }}</summary>
        <div v-for="(plan, i) in previousPlans" :key="i" class="plan-history-item">
          <div class="plan-history-header">
            <span class="plan-version-label">v{{ i + 1 }}</span>
            <span class="plan-history-title">{{ plan.title }}</span>
          </div>
          <p class="plan-history-summary">{{ plan.summary }}</p>
        </div>
      </details>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import type { PlanData } from '@/shared/types/ai.types'

const props = defineProps<{
  planData: PlanData | null
  planHistory: PlanData[]
  showImplement: boolean
}>()

defineEmits<{
  (e: 'implement'): void
}>()

const expanded = ref(true)
const previousPlans = computed(() => props.planHistory.slice(0, -1))
</script>

<style scoped>
.plan-panel {
  border-bottom: 1px solid var(--p-surface-border);
  font-size: 0.875rem;
}

.plan-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  cursor: pointer;
  user-select: none;
  gap: 8px;
}

.plan-panel-header:hover {
  background: color-mix(in srgb, var(--p-surface-ground) 50%, transparent);
}

.plan-panel-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-weight: 600;
  color: var(--p-text-color);
  min-width: 0;
  overflow: hidden;
}

.plan-panel-title i {
  color: var(--p-green-500);
  flex-shrink: 0;
}

.plan-panel-title span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.plan-panel-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.plan-panel-actions > i {
  font-size: 0.7rem;
  color: var(--p-text-muted-color);
}

.implement-btn {
  white-space: nowrap;
}

.plan-panel-body {
  padding: 0 12px 10px;
}

.plan-summary {
  color: var(--p-text-muted-color);
  margin: 0 0 8px;
  line-height: 1.5;
}

.plan-section { margin-bottom: 8px; }
.plan-section-inner { margin-top: 8px; margin-bottom: 4px; }

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

.plan-stages { list-style: none; padding-left: 0; }

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

.stage-name { font-weight: 500; }
.stage-desc { color: var(--p-text-muted-color); font-size: 0.8rem; }

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

.plan-details { margin-bottom: 6px; }

.plan-details-toggle {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  cursor: pointer;
  user-select: none;
  list-style: none;
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 2px 0;
}

.plan-details-toggle::-webkit-details-marker { display: none; }
.plan-details-toggle::before {
  content: '▶';
  font-size: 0.6rem;
  transition: transform 0.15s;
}
details[open] > .plan-details-toggle::before {
  transform: rotate(90deg);
}

.plan-version {
  font-size: 0.65rem;
  padding: 1px 5px;
  border-radius: 8px;
  font-weight: 600;
  background: color-mix(in srgb, var(--p-primary-500) 15%, transparent);
  color: var(--p-primary-500);
}

.plan-history-item {
  padding: 6px 0;
  border-top: 1px solid var(--p-surface-border);
}

.plan-history-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 2px;
}

.plan-version-label {
  font-size: 0.65rem;
  padding: 1px 5px;
  border-radius: 8px;
  font-weight: 600;
  background: var(--p-surface-100);
  color: var(--p-text-muted-color);
}

.plan-history-title {
  font-weight: 500;
  font-size: 0.8rem;
  color: var(--p-text-color);
}

.plan-history-summary {
  margin: 0;
  font-size: 0.8rem;
  color: var(--p-text-muted-color);
  line-height: 1.4;
}
</style>
