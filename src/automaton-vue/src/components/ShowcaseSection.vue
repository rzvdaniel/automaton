<script setup lang="ts">
import { ref } from 'vue'

const activeCategory = ref('games')

const categories = [
  { id: 'games', label: '🎮 Games' },
  { id: 'tools', label: '🛠 Tools' },
]

type App = {
  title: string
  description: string
  image: string
  category: string
  action: string
  color: string
}

const apps: App[] = [
  {
    title: 'Pac-Man',
    description: 'Classic arcade game. Runs instantly.',
    image: '🕹️',
    category: 'games',
    action: 'Play',
    color: '#febc2e',
  },
  {
    title: 'Snake',
    description: 'Eat the food, grow longer. Beat your high score.',
    image: '🐍',
    category: 'games',
    action: 'Play',
    color: '#3ddc84',
  },
  {
    title: 'Tetris',
    description: 'Simple Tetris clone. Clean and addictive.',
    image: '🧩',
    category: 'games',
    action: 'Play',
    color: '#4da6ff',
  },
  {
    title: 'Flappy Bird',
    description: 'How far can you go? Good luck.',
    image: '🐦',
    category: 'games',
    action: 'Play',
    color: '#7c6af5',
  },
  {
    title: 'Poker Planning',
    description: 'Agile planning poker made simple.',
    image: '🃏',
    category: 'tools',
    action: 'Open',
    color: '#7c6af5',
  },
  {
    title: 'Base64 Converter',
    description: 'Encode and decode Base64 strings instantly.',
    image: '🔐',
    category: 'tools',
    action: 'Open',
    color: '#4da6ff',
  },
  {
    title: 'JSON Formatter',
    description: 'Format, validate and beautify your JSON.',
    image: '{ }',
    category: 'tools',
    action: 'Open',
    color: '#3ddc84',
  },
  {
    title: 'UUID Generator',
    description: 'Generate UUIDs v4 in one click.',
    image: '🆔',
    category: 'tools',
    action: 'Open',
    color: '#febc2e',
  },
]

const filteredApps = () => apps.filter((a) => a.category === activeCategory.value)
</script>

<template>
  <section id="apps" class="showcase-section">
    <div class="container">
      <div class="section-header">
        <div class="section-header-left">
          <span class="section-label">
            <i class="pi pi-th-large" />
            Showcase
          </span>
          <h2 class="section-title">Try real apps built with Automaton</h2>
          <p class="section-subtitle">Open them. Use them. See how they work.</p>
        </div>
        <a href="/apps" class="view-all-link">
          View all apps <i class="pi pi-arrow-right" />
        </a>
      </div>

      <div class="category-tabs">
        <button
          v-for="cat in categories"
          :key="cat.id"
          class="cat-tab"
          :class="{ active: activeCategory === cat.id }"
          @click="activeCategory = cat.id"
        >
          {{ cat.label }}
        </button>
      </div>

      <div class="apps-grid">
        <div v-for="app in filteredApps()" :key="app.title" class="app-card">
          <div class="app-preview" :style="{ '--app-color': app.color }">
            <span class="instant-badge">⚡ Instant</span>
            <span class="app-emoji">{{ app.image }}</span>
          </div>
          <div class="app-info">
            <div class="app-built-badge">Built with Automaton</div>
            <h3 class="app-title">{{ app.title }}</h3>
            <p class="app-desc">{{ app.description }}</p>
            <div class="app-footer">
              <span class="app-author">
                <span class="author-avatar">A</span>
                by automaton
              </span>
              <a href="#" class="btn btn-secondary app-action-btn">
                {{ app.action }}
                <i class="pi pi-external-link" style="font-size:0.75rem" />
              </a>
            </div>
          </div>
        </div>
      </div>

      <div class="showcase-cta">
        <p class="showcase-cta-text">Want to build something like this?</p>
        <a href="#install" class="btn btn-primary">
          <i class="pi pi-download" />
          Install CLI
        </a>
      </div>
    </div>
  </section>
</template>

<style scoped>
.showcase-section {
  padding: 12px 0 20px;
  border-top: 1px solid var(--color-border);
}

.section-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  margin-bottom: 20px;
  gap: 20px;
}

.section-header-left {
  text-align: left;
}

.section-header-left .section-label {
  margin-bottom: 8px;
  font-size: 0.72rem;
  letter-spacing: 0.14em;
}

.section-header-left .section-title {
  font-size: clamp(1.75rem, 3.4vw, 2.35rem);
  line-height: 1.1;
  letter-spacing: -0.03em;
}

.section-header-left .section-subtitle {
  margin-top: 8px;
  font-size: 0.98rem;
  max-width: 460px;
}

.view-all-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--color-accent);
  text-decoration: none;
  white-space: nowrap;
  margin-top: 0;
  transition: opacity 0.2s;
}

.view-all-link:hover {
  opacity: 0.75;
}

.showcase-built-by {
  margin-top: 8px;
  font-size: 0.8rem;
  color: var(--color-accent);
  font-weight: 600;
}

.category-tabs {
  display: flex;
  justify-content: flex-start;
  gap: 10px;
  margin-bottom: 26px;
}

.cat-tab {
  padding: 8px 18px;
  border-radius: 999px;
  font-size: 0.84rem;
  font-weight: 600;
  cursor: pointer;
  background: rgba(255, 255, 255, 0.02);
  border: 1px solid var(--color-border);
  color: var(--color-text-muted);
  transition: all 0.2s;
  line-height: 1;
}

.cat-tab.active,
.cat-tab:hover {
  background: rgba(124, 106, 245, 0.16);
  border-color: rgba(124, 106, 245, 0.4);
  color: var(--color-text);
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 20px;
}

.app-card {
  border-radius: var(--radius);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  overflow: hidden;
  transition: border-color 0.2s, transform 0.2s, box-shadow 0.2s;
  cursor: pointer;
}

.app-card:hover {
  transform: translateY(-3px);
  border-color: rgba(255, 255, 255, 0.14);
  box-shadow: 0 16px 40px rgba(0, 0, 0, 0.3);
}

.app-preview {
  position: relative;
  height: 140px;
  background: linear-gradient(
    135deg,
    color-mix(in srgb, var(--app-color) 12%, var(--color-surface-2)),
    var(--color-surface-2)
  );
  display: flex;
  align-items: center;
  justify-content: center;
  border-bottom: 1px solid var(--color-border);
}

.instant-badge {
  position: absolute;
  top: 10px;
  left: 10px;
  padding: 3px 8px;
  border-radius: 6px;
  font-size: 0.7rem;
  font-weight: 700;
  background: var(--color-accent);
  color: #fff;
  letter-spacing: 0.02em;
}

.app-emoji {
  font-size: 3rem;
}

.app-info {
  padding: 16px;
}

.app-built-badge {
  display: inline-block;
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--color-accent);
  margin-bottom: 6px;
  letter-spacing: 0.02em;
}

.app-title {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--color-text);
  margin-bottom: 6px;
}

.app-desc {
  font-size: 0.82rem;
  color: var(--color-text-muted);
  line-height: 1.5;
  margin-bottom: 16px;
}

.app-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.app-author {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.78rem;
  color: var(--color-text-muted);
}

.author-avatar {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: var(--color-accent-soft);
  border: 1px solid rgba(124, 106, 245, 0.3);
  color: var(--color-accent);
  font-size: 0.7rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
}

.app-action-btn {
  padding: 5px 12px;
  font-size: 0.78rem;
  gap: 4px;
}

.showcase-cta {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  margin-top: 48px;
}

.showcase-cta-text {
  font-size: 0.95rem;
  color: var(--color-text-muted);
  font-weight: 500;
}

@media (max-width: 1024px) {
  .apps-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 760px) {
  .section-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 10px;
  }

  .category-tabs {
    flex-wrap: wrap;
  }
}

@media (max-width: 600px) {
  .apps-grid {
    grid-template-columns: 1fr;
    max-width: 360px;
    margin: 0 auto;
  }
}
</style>
