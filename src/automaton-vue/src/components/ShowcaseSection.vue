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
    description: 'The classic arcade game. Built with Automaton.',
    image: '🕹️',
    category: 'games',
    action: 'Play',
    color: '#febc2e',
  },
  {
    title: 'Snake',
    description: 'Eat the food, grow longer, beat your high score.',
    image: '🐍',
    category: 'games',
    action: 'Play',
    color: '#3ddc84',
  },
  {
    title: 'Tetris',
    description: 'A simple Tetris clone. Clean and addictive.',
    image: '🧩',
    category: 'games',
    action: 'Play',
    color: '#4da6ff',
  },
  {
    title: 'Flappy Bird',
    description: 'How far can you go? Good luck!',
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
        <span class="section-label">
          <i class="pi pi-th-large" />
          Showcase
        </span>
        <h2 class="section-title">Explore what's built with Automaton</h2>
        <p class="section-subtitle">Real apps from real developers. Open, simple, and useful.</p>
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
            <span class="app-emoji">{{ app.image }}</span>
          </div>
          <div class="app-info">
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
        <a href="/apps" class="btn btn-secondary">
          View all apps
          <i class="pi pi-arrow-right" />
        </a>
      </div>
    </div>
  </section>
</template>

<style scoped>
.showcase-section {
  padding: 100px 0;
  border-top: 1px solid var(--color-border);
}

.section-header {
  text-align: center;
  margin-bottom: 40px;
}

.section-header .section-subtitle {
  margin: 12px auto 0;
}

.category-tabs {
  display: flex;
  justify-content: center;
  gap: 8px;
  margin-bottom: 48px;
}

.cat-tab {
  padding: 8px 20px;
  border-radius: 20px;
  font-size: 0.88rem;
  font-weight: 600;
  cursor: pointer;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  color: var(--color-text-muted);
  transition: all 0.2s;
}

.cat-tab.active,
.cat-tab:hover {
  background: var(--color-accent-soft);
  border-color: rgba(124, 106, 245, 0.4);
  color: var(--color-accent);
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

.app-emoji {
  font-size: 3rem;
}

.app-info {
  padding: 16px;
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
  justify-content: center;
  margin-top: 48px;
}

@media (max-width: 1024px) {
  .apps-grid {
    grid-template-columns: repeat(2, 1fr);
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
