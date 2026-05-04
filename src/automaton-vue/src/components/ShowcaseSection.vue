<script setup lang="ts">
import { computed, ref } from 'vue'
import flappyBirdImage from '@/assets/images/flappy-birds.png'
import pacManImage from '@/assets/images/pac-man.png'
import snakeImage from '@/assets/images/snake.png'
import tetrisImage from '@/assets/images/tetris.png'

const activeCategory = ref('games')

const categories = [
  { id: 'games', label: 'Games', icon: 'pi pi-microsoft' },
  { id: 'tools', label: 'Tools', icon: 'pi pi-wrench' },
]

type App = {
  title: string
  description: string
  image?: string
  icon?: string
  category: string
  action: string
  color: string
  tagline: string
}

const apps: App[] = [
  {
    title: 'Pac-Man',
    description: 'Classic arcade game. Runs instantly.',
    image: pacManImage,
    category: 'games',
    action: 'Play now',
    color: '#febc2e',
    tagline: 'Built with Automaton',
  },
  {
    title: 'Snake',
    description: 'Eat, grow, and beat your high score.',
    image: snakeImage,
    category: 'games',
    action: 'Play now',
    color: '#3ddc84',
    tagline: 'Built with Automaton',
  },
  {
    title: 'Tetris',
    description: 'A simple Tetris clone. Clean and addictive.',
    image: tetrisImage,
    category: 'games',
    action: 'Play now',
    color: '#4da6ff',
    tagline: 'Built with Automaton',
  },
  {
    title: 'Flappy Bird',
    description: 'How far can you go? Good luck.',
    image: flappyBirdImage,
    category: 'games',
    action: 'Play now',
    color: '#7c6af5',
    tagline: 'Built with Automaton',
  },
  {
    title: 'Poker Planning',
    description: 'Agile planning poker made simple.',
    icon: 'pi pi-users',
    category: 'tools',
    action: 'Open',
    color: '#7c6af5',
    tagline: 'Team tool',
  },
  {
    title: 'Base64 Converter',
    description: 'Encode and decode Base64 strings instantly.',
    icon: 'pi pi-lock',
    category: 'tools',
    action: 'Open',
    color: '#4da6ff',
    tagline: 'Developer tool',
  },
  {
    title: 'JSON Formatter',
    description: 'Format, validate and beautify your JSON.',
    icon: 'pi pi-code',
    category: 'tools',
    action: 'Open',
    color: '#3ddc84',
    tagline: 'Developer tool',
  },
  {
    title: 'UUID Generator',
    description: 'Generate UUIDs v4 in one click.',
    icon: 'pi pi-hashtag',
    category: 'tools',
    action: 'Open',
    color: '#febc2e',
    tagline: 'Utility',
  },
]

const filteredApps = computed(() => apps.filter((app) => app.category === activeCategory.value))
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
          <i :class="cat.icon" />
          <span>{{ cat.label }}</span>
        </button>
      </div>

      <div class="apps-grid">
        <article v-for="app in filteredApps" :key="app.title" class="app-card">
          <div class="app-preview" :style="{ '--app-color': app.color }">
            <span class="instant-badge">
              <i class="pi pi-bolt" />
              Instant
            </span>
            <img v-if="app.image" :src="app.image" :alt="`${app.title} preview`" class="app-thumbnail" />
            <div v-else class="app-tool-art" aria-hidden="true">
              <div class="tool-grid"></div>
              <span class="tool-icon-wrap">
                <i :class="app.icon" />
              </span>
            </div>
          </div>
          <div class="app-info">
            <!-- <div class="app-built-badge">{{ app.tagline }}</div> -->
            <h3 class="app-title">{{ app.title }}</h3>
            <p class="app-desc">{{ app.description }}</p>
            <div class="app-footer">
              <span class="app-author">Built with Automaton</span>
              <a href="#" class="btn btn-secondary app-action-btn">
                {{ app.action }}
                <span class="app-action-arrow" aria-hidden="true">▶</span>
              </a>
            </div>
          </div>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.showcase-section {
  padding: 30px 0 10px;
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
  display: inline-flex;
  align-items: center;
  gap: 8px;
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
  background: linear-gradient(135deg, rgba(124, 106, 245, 0.26), rgba(124, 106, 245, 0.14));
  border-color: rgba(124, 106, 245, 0.46);
  color: #fff;
  box-shadow: 0 10px 24px rgba(124, 106, 245, 0.16);
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 18px;
}

.app-card {
  display: flex;
  flex-direction: column;
  border-radius: var(--radius);
  background: linear-gradient(180deg, rgba(20, 20, 28, 0.95), rgba(14, 14, 20, 0.98));
  border: 1px solid var(--color-border);
  overflow: hidden;
  transition: border-color 0.2s, transform 0.2s, box-shadow 0.2s, background 0.2s;
  cursor: pointer;
}

.app-card:hover {
  transform: translateY(-3px);
  border-color: rgba(255, 255, 255, 0.14);
  box-shadow: 0 18px 40px rgba(0, 0, 0, 0.35);
}

.app-preview {
  position: relative;
  height: 140px;
  background:
    radial-gradient(circle at top right, color-mix(in srgb, var(--app-color) 22%, transparent), transparent 38%),
    linear-gradient(180deg, rgba(11, 14, 25, 0.96), rgba(10, 11, 17, 0.98));
  display: flex;
  align-items: center;
  justify-content: center;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
}

.instant-badge {
  position: absolute;
  top: 10px;
  left: 10px;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 4px 9px;
  border-radius: 6px;
  font-size: 0.7rem;
  font-weight: 700;
  background: rgba(18, 22, 31, 0.92);
  color: #fff;
  letter-spacing: 0.02em;
  z-index: 1;
}

.app-thumbnail {
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: center;
  border-radius: 12px;
  display: block;
  box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.05);
}

.app-tool-art {
  position: relative;
  width: 100%;
  height: 100%;
  border-radius: 12px;
  overflow: hidden;
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--app-color) 26%, rgba(255, 255, 255, 0.04)), rgba(12, 14, 22, 0.96)),
    linear-gradient(180deg, rgba(255, 255, 255, 0.03), transparent);
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.tool-grid {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(255, 255, 255, 0.05) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.05) 1px, transparent 1px);
  background-size: 20px 20px;
  opacity: 0.3;
}

.tool-icon-wrap {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 2.75rem;
  color: var(--app-color);
  text-shadow: 0 0 28px color-mix(in srgb, var(--app-color) 34%, transparent);
}

.app-info {
  padding: 16px 14px 12px;
  display: flex;
  flex: 1;
  flex-direction: column;
}

.app-built-badge {
  display: inline-block;
  font-size: 0.76rem;
  font-weight: 600;
  color: rgba(255, 255, 255, 0.78);
  margin-bottom: 8px;
  letter-spacing: 0.02em;
}

.app-title {
  font-size: 1.2rem;
  font-weight: 700;
  color: var(--color-text);
  margin-bottom: 8px;
}

.app-desc {
  font-size: 0.92rem;
  color: rgba(234, 236, 245, 0.84);
  line-height: 1.5;
  min-height: 3em;
  margin-bottom: 18px;
}

.app-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: auto;
  padding-top: 12px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.app-author {
  font-size: 0.78rem;
  color: rgba(255, 255, 255, 0.72);
}

.app-action-btn {
  padding: 7px 12px;
  font-size: 0.78rem;
  gap: 6px;
  border-color: rgba(124, 106, 245, 0.34);
  background: rgba(124, 106, 245, 0.18);
  color: #fff;
}

.app-action-btn:hover {
  background: rgba(124, 106, 245, 0.3);
  border-color: rgba(124, 106, 245, 0.45);
}

.app-action-arrow {
  font-size: 0.7rem;
  line-height: 1;
  transform: translateX(1px);
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

  .app-title {
    font-size: 1.08rem;
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
