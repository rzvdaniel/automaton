<script setup lang="ts">
const reasons = [
  {
    icon: 'pi-lock-open',
    title: 'No lock-in',
    description: 'Your code is yours. Runs anywhere.',
    color: 'green',
  },
  {
    icon: 'pi-wrench',
    title: 'Bring your own AI',
    description: 'Use Copilot, Claude, or anything else.',
    color: 'accent',
  },
  {
    icon: 'pi-server',
    title: 'Cloud or local',
    description: 'Deploy to Automaton Cloud or your own Runner.',
    color: 'blue',
  },
  {
    icon: 'pi-bolt',
    title: 'Zero DevOps',
    description: 'No containers. No pipelines. No config files.',
    color: 'green',
  },
  {
    icon: 'pi-share-alt',
    title: 'Instant sharing',
    description: 'Every app gets a public URL automatically.',
    color: 'accent',
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
        <h2 class="section-title">Built for how developers work today</h2>
        <p class="section-subtitle">Not a builder. Not a platform. Just a runtime that gets out of your way.</p>
      </div>

      <div class="reasons-strip">
        <div v-for="reason in reasons" :key="reason.title" class="reason-item">
          <div class="reason-icon-wrap" :class="`reason-${reason.color}`">
            <i :class="`pi ${reason.icon}`" />
          </div>
          <div class="reason-body">
            <h3 class="reason-title">{{ reason.title }}</h3>
            <p class="reason-desc">{{ reason.description }}</p>
          </div>
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

.reasons-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  overflow: hidden;
}

.reason-item {
  flex: 1;
  min-width: 180px;
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 24px 20px;
  border-right: 1px solid var(--color-border);
  transition: background 0.2s;
}

.reason-item:last-child {
  border-right: none;
}

.reason-item:hover {
  background: var(--color-surface-2);
}

.reason-icon-wrap {
  flex-shrink: 0;
  width: 36px;
  height: 36px;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1rem;
  margin-top: 2px;
}

.reason-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
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
  font-size: 0.88rem;
  font-weight: 700;
  color: var(--color-text);
}

.reason-desc {
  font-size: 0.8rem;
  color: var(--color-text-muted);
  line-height: 1.5;
}

@media (max-width: 900px) {
  .reasons-strip {
    flex-direction: column;
  }

  .reason-item {
    border-right: none;
    border-bottom: 1px solid var(--color-border);
    min-width: 100%;
  }

  .reason-item:last-child {
    border-bottom: none;
  }
}
</style>
