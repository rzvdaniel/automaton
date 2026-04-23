<script setup lang="ts">
const steps = [
  {
    number: '01',
    icon: 'pi-code',
    title: 'Build',
    subtitle: 'Build with your tools',
    description: 'Use Copilot, Claude, or any IDE. Follow a simple /ui + /backend structure.',
    detail: null,
    color: 'accent',
  },
  {
    number: '02',
    icon: 'pi-upload',
    title: 'Deploy',
    subtitle: 'Run one command',
    description: 'We detect your app and deploy it instantly.',
    detail: null,
    code: 'automaton deploy',
    color: 'blue',
  },
  {
    number: '03',
    icon: 'pi-globe',
    title: 'Done',
    subtitle: 'Your app is live',
    description: 'Get a public URL. Backend runs on cloud or your own Runner.',
    detail: null,
    color: 'green',
  },
]
</script>

<template>
  <section id="how-it-works" class="how-section">
    <div class="container">
      <div class="section-header">
        <h2 class="section-title">From code to live in seconds</h2>
        <p class="section-subtitle">No pipelines. No setup. Just deploy.</p>
      </div>

      <div class="steps-card">
        <template v-for="(step, index) in steps" :key="step.number">
          <div class="step-item" :class="`step-${step.color}`">
            <div class="step-circle" :class="`circle-${step.color}`">{{ index + 1 }}</div>
            <div class="step-icon-wrap" :class="`icon-${step.color}`">
              <i :class="`pi ${step.icon}`" />
            </div>
            <div class="step-body">
              <div class="step-eyebrow">{{ step.title }}</div>
              <h3 class="step-title">{{ step.subtitle }}</h3>
              <p class="step-desc">{{ step.description }}</p>
              <div v-if="step.code" class="step-code">
                <i class="pi pi-bolt" style="font-size:0.75rem; color: var(--color-blue)" />
                {{ step.code }}
              </div>
            </div>
          </div>
          <div v-if="index < steps.length - 1" class="step-arrow">
            <i class="pi pi-arrow-right" />
          </div>
        </template>
      </div>
    </div>
  </section>
</template>

<style scoped>
.how-section {
  padding: 100px 0;
  border-top: 1px solid var(--color-border);
}

.section-header {
  text-align: center;
  margin-bottom: 40px;
}

.section-header .section-subtitle {
  margin: 8px auto 0;
}

/* Single card wrapping all steps */
.steps-card {
  display: flex;
  align-items: center;
  gap: 0;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: 40px 32px;
}

.step-item {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 0 16px;
}

.step-arrow {
  flex-shrink: 0;
  color: var(--color-text-muted);
  opacity: 0.4;
  font-size: 1.1rem;
  padding: 0 8px;
}

.step-circle {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  font-size: 0.85rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
}

.circle-accent { background: var(--color-accent-soft); color: var(--color-accent); border: 1px solid rgba(124, 106, 245, 0.3); }
.circle-blue   { background: var(--color-blue-soft);   color: var(--color-blue);   border: 1px solid rgba(77, 166, 255, 0.3); }
.circle-green  { background: var(--color-green-soft);  color: var(--color-green);  border: 1px solid rgba(61, 220, 132, 0.3); }

.step-icon-wrap {
  width: 44px;
  height: 44px;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.2rem;
}

.icon-accent { background: var(--color-accent-soft); color: var(--color-accent); }
.icon-blue   { background: var(--color-blue-soft);   color: var(--color-blue); }
.icon-green  { background: var(--color-green-soft);  color: var(--color-green); }

.step-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.step-eyebrow {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--color-text-muted);
}

.step-accent .step-eyebrow { color: var(--color-accent); }
.step-blue   .step-eyebrow { color: var(--color-blue); }
.step-green  .step-eyebrow { color: var(--color-green); }

.step-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--color-text);
  margin: 0;
}

.step-desc {
  font-size: 0.85rem;
  color: var(--color-text-muted);
  line-height: 1.6;
  margin-top: 4px;
}

.step-code {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-top: 8px;
  padding: 6px 12px;
  border-radius: var(--radius-sm);
  background: rgba(0, 0, 0, 0.4);
  font-family: var(--font-mono);
  font-size: 0.82rem;
  color: var(--color-text);
  border: 1px solid rgba(77, 166, 255, 0.2);
  width: fit-content;
}

@media (max-width: 900px) {
  .steps-card {
    flex-direction: column;
    align-items: flex-start;
    gap: 0;
    padding: 28px 24px;
  }

  .step-item {
    padding: 16px 0;
    width: 100%;
  }

  .step-item:not(:last-child) {
    border-bottom: 1px solid var(--color-border);
  }

  .step-arrow {
    display: none;
  }
}
</style>
