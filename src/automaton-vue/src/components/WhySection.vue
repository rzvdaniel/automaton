<script setup lang="ts">
const reasons = [
  {
    icon: 'pi-lock',
    title: 'No lock-in',
    description: 'Your code is yours. Runs anywhere.',
    color: 'green',
  },
  {
    icon: 'pi-sparkles',
    title: 'Bring your own AI',
    description: 'Use Copilot, Claude, or anything else.',
    color: 'accent',
  },
  {
    icon: 'pi-server',
    title: 'Cloud or local',
    description: 'Deploy to Automaton Cloud or your own Runner.',
    color: 'green',
  },
  {
    icon: 'pi-bolt',
    title: 'Zero DevOps',
    description: 'No containers. No pipelines. No config files.',
    color: 'accent',
  },
  {
    icon: 'pi-share-alt',
    title: 'Instant sharing',
    description: 'Every app gets a public URL automatically.',
    color: 'green',
  },
]
</script>

<template>
  <section id="why" class="why-section">
    <div class="container">
      <div class="reasons-strip">
        <article v-for="reason in reasons" :key="reason.title" class="reason-item">
          <div class="reason-icon-wrap" :class="`reason-${reason.color}`">
            <i :class="`pi ${reason.icon}`" />
          </div>
          <div class="reason-body">
            <h3 class="reason-title">{{ reason.title }}</h3>
            <p class="reason-desc">{{ reason.description }}</p>
          </div>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.why-section {
  padding: 5px 0 10px;
}

.reasons-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
}

.reason-item {
  flex: 1;
  min-width: 170px;
  display: flex;
  align-items: flex-start;
  gap: 14px;
  padding: 14px 8px;
}

.reason-icon-wrap {
  flex-shrink: 0;
  width: 44px;
  height: 44px;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.65rem;
  line-height: 1;
  margin-top: -1px;
}

.reason-icon-wrap :deep(.pi) {
  font-size: inherit;
}

.reason-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.reason-green {
  color: #18dc7b;
  text-shadow: 0 0 16px rgba(24, 220, 123, 0.18);
}

.reason-accent {
  color: #b06cff;
  text-shadow: 0 0 16px rgba(176, 108, 255, 0.22);
}

.reason-title {
  font-size: 0.88rem;
  font-weight: 700;
  color: var(--color-text);
}

.reason-desc {
  font-size: 0.84rem;
  line-height: 1.5;
  color: rgba(233, 236, 245, 0.68);
}

@media (max-width: 900px) {
  .reasons-strip {
    flex-direction: column;
  }

  .reason-item {
    padding-inline: 0;
  }
}
</style>
