<script setup lang="ts">
import { publicAppConfig } from '@/config/publicAppConfig'

const steps = [
  {
    icon: 'pi-code',
    title: 'Build with your tools',
    description: 'Use Copilot, Claude, or any IDE. Just follow a simple /ui + /backend structure.',
  },
  {
    icon: 'pi-upload',
    title: 'Run one command',
    description: 'We detect your app and deploy it instantly.',
    code: 'automaton deploy',
  },
  {
    icon: 'pi-globe',
    title: 'Your app is live',
    description: 'Get a public URL. Backend runs on cloud or your own Runner.',
  },
]
</script>

<template>
  <section id="how-it-works" class="how-section">
    <div class="container">
      <div class="steps-card">
        <div class="section-copy">
          <h2 class="section-title">From code to live in seconds</h2>
          <p class="section-subtitle">No pipelines. No setup. Just deploy.</p>
        </div>

        <div class="steps-row">
          <template v-for="(step, index) in steps" :key="step.title">
            <article class="step-item">
              <div class="step-number">{{ index + 1 }}</div>
              <div class="step-icon-wrap">
                <i :class="`pi ${step.icon}`" />
              </div>
              <div class="step-body">
                <h3 class="step-title">{{ step.title }}</h3>
                <p class="step-desc">{{ step.description }}</p>
                <div v-if="step.code" class="step-code">
                  <span class="step-code-prompt">$</span>
                  {{ step.code }}
                </div>
              </div>
            </article>
            <div v-if="index < steps.length - 1" class="step-arrow">
              <i class="pi pi-arrow-right" />
            </div>
          </template>
        </div>

        <div class="steps-footer">
          <a href="/cli#workflow" class="steps-link">
            See the full CLI workflow
            <i class="pi pi-arrow-right" />
          </a>
          <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="steps-link steps-link-muted">
            Read llms.txt
            <i class="pi pi-external-link" />
          </a>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.how-section {
  padding: 15px 0 15px;
}

.steps-card {
  padding: 24px 26px 26px;
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  background:
    radial-gradient(circle at 96% 18%, rgba(124, 106, 245, 0.08), transparent 34%),
    linear-gradient(180deg, rgba(13, 17, 25, 0.96), rgba(10, 13, 19, 0.98));
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.02);
}

.section-copy {
  margin-bottom: 26px;
}

.section-title {
  font-size: 1.45rem;
  line-height: 1.2;
  letter-spacing: 0;
}

.section-subtitle {
  margin-top: 6px;
  font-size: 0.98rem;
  color: rgba(233, 236, 245, 0.72);
}

.steps-row {
  display: flex;
  align-items: center;
  gap: 20px;
}

.steps-footer {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-top: 22px;
  padding-top: 18px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  flex-wrap: wrap;
}

.steps-link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: #d7d2ff;
  font-size: 0.86rem;
  font-weight: 600;
}

.steps-link-muted {
  color: rgba(233, 236, 245, 0.74);
}

.step-item {
  flex: 1;
  min-width: 0;
  display: grid;
  grid-template-columns: 38px 70px minmax(0, 1fr);
  align-items: start;
  gap: 14px;
}

.step-number {
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: #4f36b3;
  color: #ffffff;
  font-size: 0.9rem;
  font-weight: 700;
  line-height: 1;
}

.step-arrow {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: rgba(124, 106, 245, 0.44);
  font-size: 1rem;
  margin-top: 24px;
}

.step-icon-wrap {
  position: relative;
  width: 68px;
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #8f6cff;
  font-size: 2.55rem;
  line-height: 1;
  text-shadow: 0 0 18px rgba(143, 108, 255, 0.26);
}

.step-icon-wrap :deep(.pi) {
  font-size: inherit;
}

.step-body {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
}

.step-title {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--color-text);
  line-height: 1.25;
}

.step-desc {
  font-size: 0.82rem;
  line-height: 1.5;
  color: rgba(233, 236, 245, 0.64);
}

.step-code {
  order: -1;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  width: min(100%, 200px);
  margin-bottom: 1px;
  padding: 5px 10px;
  border-radius: 4px;
  border: 1px solid rgba(124, 106, 245, 0.22);
  background: rgba(15, 17, 27, 0.82);
  color: #b897ff;
  font-family: var(--font-mono);
  font-size: 0.76rem;
  white-space: nowrap;
}

.step-code-prompt {
  color: rgba(255, 255, 255, 0.72);
}

@media (max-width: 900px) {
  .steps-row {
    flex-direction: column;
    align-items: stretch;
    gap: 0;
  }

  .step-item {
    width: 100%;
    padding: 18px 0;
  }

  .step-item:not(:last-child) {
    border-bottom: 1px solid var(--color-border);
  }

  .step-arrow {
    display: none;
  }
}

@media (max-width: 640px) {
  .steps-card {
    padding: 22px 20px;
  }

  .step-item {
    grid-template-columns: 34px minmax(0, 1fr);
    gap: 14px;
  }

  .step-icon-wrap {
    grid-column: 2;
    grid-row: 1;
    margin-left: 4px;
  }

  .step-body {
    grid-column: 2;
  }
}
</style>
