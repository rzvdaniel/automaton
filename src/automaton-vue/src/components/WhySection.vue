<script setup lang="ts">
const reasons = [
  {
    icon: 'pi-lock-open',
    title: 'No lock-in',
    description: 'Your app runs anywhere. Standard HTML, JS, and Python. Export anytime. We never own your code.',
    color: 'green',
  },
  {
    icon: 'pi-wrench',
    title: 'Bring your own AI',
    description: 'Build with GitHub Copilot, Claude Code, or any tool you prefer. Automaton is your runtime, not your IDE.',
    color: 'accent',
  },
  {
    icon: 'pi-server',
    title: 'Cloud or local — your choice',
    description: 'Deploy to Automaton Cloud for instant hosting, or connect your own Runner for full control. Switch anytime.',
    color: 'blue',
  },
  {
    icon: 'pi-bolt',
    title: 'Zero DevOps',
    description: 'No Kubernetes, no Docker required, no CI pipelines to configure. Run one command and your app is live.',
    color: 'green',
  },
  {
    icon: 'pi-share-alt',
    title: 'Instant sharing',
    description: 'Every app gets a public URL automatically. Share your project with a single link — no extra setup.',
    color: 'accent',
  },
  {
    icon: 'pi-code',
    title: 'Structured runtime',
    description: 'Opinionated /ui + /backend structure means your app always works consistently across machines and runners.',
    color: 'blue',
  },
]
</script>

<template>
  <section id="why" class="why-section">
    <div class="container">
      <div class="section-header">
        <span class="section-label">
          <i class="pi pi-star" />
          Why Automaton
        </span>
        <h2 class="section-title">Infrastructure for the AI-era developer</h2>
        <p class="section-subtitle">Not a builder. Not a platform. A runtime layer that gets out of your way.</p>
      </div>

      <div class="reasons-grid">
        <div v-for="reason in reasons" :key="reason.title" class="reason-card">
          <div class="reason-icon-wrap" :class="`reason-${reason.color}`">
            <i :class="`pi ${reason.icon}`" />
          </div>
          <h3 class="reason-title">{{ reason.title }}</h3>
          <p class="reason-desc">{{ reason.description }}</p>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.why-section {
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

.reasons-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 24px;
}

.reason-card {
  padding: 28px 24px;
  border-radius: var(--radius);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  transition: border-color 0.2s, transform 0.2s;
}

.reason-card:hover {
  transform: translateY(-2px);
  border-color: rgba(255, 255, 255, 0.12);
}

.reason-icon-wrap {
  width: 40px;
  height: 40px;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 16px;
  font-size: 1.1rem;
}

.reason-green {
  background: var(--color-green-soft);
  color: var(--color-green);
}

.reason-accent {
  background: var(--color-accent-soft);
  color: var(--color-accent);
}

.reason-blue {
  background: var(--color-blue-soft);
  color: var(--color-blue);
}

.reason-title {
  font-size: 0.98rem;
  font-weight: 700;
  color: var(--color-text);
  margin-bottom: 8px;
}

.reason-desc {
  font-size: 0.875rem;
  color: var(--color-text-muted);
  line-height: 1.65;
}

@media (max-width: 900px) {
  .reasons-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 600px) {
  .reasons-grid {
    grid-template-columns: 1fr;
  }
}
</style>
