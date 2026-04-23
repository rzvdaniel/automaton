<script setup lang="ts">
const steps = [
  {
    number: '01',
    icon: 'pi-code',
    title: 'Build with your tools',
    description: 'Use GitHub Copilot, Claude Code, Cursor, or any IDE you love. Structure your app with a /ui folder and optionally a /backend folder.',
    detail: 'No Automaton-specific code required',
    color: 'accent',
  },
  {
    number: '02',
    icon: 'pi-upload',
    title: 'Run one command',
    description: 'From your project root, run the Automaton CLI. It detects your stack, validates the structure, and deploys instantly.',
    detail: null,
    code: 'automaton deploy',
    color: 'blue',
  },
  {
    number: '03',
    icon: 'pi-globe',
    title: 'Done — share your app',
    description: 'Your app is live with a public URL. Backend runs on Automaton Cloud or your own Runner. Share it with anyone, instantly.',
    detail: 'Live URL · Backend connected · Zero config',
    color: 'green',
  },
]
</script>

<template>
  <section id="how-it-works" class="how-section">
    <div class="container">
      <div class="section-header">
        <span class="section-label">
          <i class="pi pi-bolt" />
          How it works
        </span>
        <h2 class="section-title">From code to live in 3 steps</h2>
        <p class="section-subtitle">No DevOps. No containers. No config files. Just code and deploy.</p>
      </div>

      <div class="steps-grid">
        <div
          v-for="step in steps"
          :key="step.number"
          class="step-card"
          :class="`step-${step.color}`"
        >
          <div class="step-number">{{ step.number }}</div>
          <div class="step-icon-wrap">
            <i :class="`pi ${step.icon}`" class="step-icon" />
          </div>
          <h3 class="step-title">{{ step.title }}</h3>
          <p class="step-desc">{{ step.description }}</p>
          <div v-if="step.code" class="step-code">
            <span class="step-prompt">$</span> {{ step.code }}
          </div>
          <div v-if="step.detail" class="step-detail">{{ step.detail }}</div>
        </div>
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
  margin-bottom: 64px;
}

.section-header .section-subtitle {
  margin: 12px auto 0;
}

.steps-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 24px;
}

.step-card {
  position: relative;
  padding: 36px 28px;
  border-radius: var(--radius);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  transition: border-color 0.2s, transform 0.2s;
}

.step-card:hover {
  transform: translateY(-2px);
}

.step-accent {
  border-top: 2px solid var(--color-accent);
}

.step-blue {
  border-top: 2px solid var(--color-blue);
}

.step-green {
  border-top: 2px solid var(--color-green);
}

.step-number {
  position: absolute;
  top: 20px;
  right: 24px;
  font-size: 3rem;
  font-weight: 800;
  color: rgba(255, 255, 255, 0.04);
  font-family: var(--font-mono);
  line-height: 1;
  user-select: none;
}

.step-icon-wrap {
  width: 44px;
  height: 44px;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 20px;
}

.step-accent .step-icon-wrap {
  background: var(--color-accent-soft);
}

.step-blue .step-icon-wrap {
  background: var(--color-blue-soft);
}

.step-green .step-icon-wrap {
  background: var(--color-green-soft);
}

.step-icon {
  font-size: 1.2rem;
}

.step-accent .step-icon { color: var(--color-accent); }
.step-blue .step-icon { color: var(--color-blue); }
.step-green .step-icon { color: var(--color-green); }

.step-title {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--color-text);
  margin-bottom: 12px;
}

.step-desc {
  font-size: 0.9rem;
  color: var(--color-text-muted);
  line-height: 1.65;
}

.step-code {
  margin-top: 16px;
  padding: 10px 16px;
  border-radius: var(--radius-sm);
  background: rgba(0, 0, 0, 0.4);
  font-family: var(--font-mono);
  font-size: 0.85rem;
  color: var(--color-text);
  border: 1px solid rgba(77, 166, 255, 0.2);
}

.step-prompt {
  color: var(--color-blue);
  font-weight: 700;
}

.step-detail {
  margin-top: 16px;
  font-size: 0.78rem;
  color: var(--color-text-muted);
  font-weight: 600;
  letter-spacing: 0.02em;
}

.step-accent .step-detail { color: var(--color-accent); }
.step-blue .step-detail { color: var(--color-blue); }
.step-green .step-detail { color: var(--color-green); }

@media (max-width: 900px) {
  .steps-grid {
    grid-template-columns: 1fr;
    max-width: 480px;
    margin: 0 auto;
  }
}
</style>
