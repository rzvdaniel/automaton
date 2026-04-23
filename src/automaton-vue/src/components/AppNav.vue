<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'

const mobileMenuOpen = ref(false)

const navLinks = [
  { label: 'Apps', href: '/apps' },
  { label: 'Docs', href: '/docs' },
  { label: 'Pricing', href: '/pricing' },
  { label: 'GitHub', href: 'https://github.com', external: true },
]
</script>

<template>
  <header class="nav-header">
    <div class="container nav-inner">
      <RouterLink to="/" class="nav-logo">
        <span class="logo-icon">⚡</span>
        <span class="logo-text">Automaton</span>
      </RouterLink>

      <nav class="nav-links desktop-only">
        <a
          v-for="link in navLinks"
          :key="link.label"
          :href="link.href"
          :target="link.external ? '_blank' : undefined"
          class="nav-link"
        >
          {{ link.label }}
        </a>
      </nav>

      <div class="nav-actions desktop-only">
        <a href="#" class="btn btn-secondary btn-sm">Sign in</a>
        <a href="#install" class="btn btn-primary btn-sm">Get Started</a>
      </div>

      <button class="mobile-menu-btn mobile-only" @click="mobileMenuOpen = !mobileMenuOpen">
        <i :class="mobileMenuOpen ? 'pi pi-times' : 'pi pi-bars'" />
      </button>
    </div>

    <div v-if="mobileMenuOpen" class="mobile-menu">
      <a v-for="link in navLinks" :key="link.label" :href="link.href" class="mobile-nav-link">
        {{ link.label }}
      </a>
      <div class="mobile-menu-actions">
        <a href="#" class="btn btn-secondary" style="width:100%; justify-content:center">Sign in</a>
        <a href="#install" class="btn btn-primary" style="width:100%; justify-content:center">Get Started</a>
      </div>
    </div>
  </header>
</template>

<style scoped>
.nav-header {
  position: sticky;
  top: 0;
  z-index: 100;
  background: rgba(10, 10, 15, 0.85);
  backdrop-filter: blur(12px);
  border-bottom: 1px solid var(--color-border);
}

.nav-inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 64px;
}

.nav-logo {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 700;
  font-size: 1.1rem;
  color: var(--color-text);
  text-decoration: none;
}

.logo-icon {
  font-size: 1.3rem;
}

.logo-text {
  letter-spacing: -0.01em;
}

.nav-links {
  display: flex;
  align-items: center;
  gap: 4px;
}

.nav-link {
  padding: 6px 14px;
  border-radius: var(--radius-sm);
  color: var(--color-text-muted);
  font-size: 0.9rem;
  font-weight: 500;
  transition: color 0.2s, background 0.2s;
  text-decoration: none;
}

.nav-link:hover {
  color: var(--color-text);
  background: var(--color-surface-2);
}

.nav-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.btn-sm {
  padding: 8px 16px;
  font-size: 0.85rem;
}

.desktop-only {
  display: flex;
}

.mobile-only {
  display: none;
}

.mobile-menu-btn {
  background: none;
  border: none;
  color: var(--color-text);
  font-size: 1.2rem;
  cursor: pointer;
  padding: 8px;
}

.mobile-menu {
  display: flex;
  flex-direction: column;
  padding: 16px 24px 24px;
  gap: 4px;
  border-top: 1px solid var(--color-border);
  background: var(--color-bg);
}

.mobile-nav-link {
  padding: 12px 8px;
  color: var(--color-text-muted);
  font-size: 1rem;
  font-weight: 500;
  border-bottom: 1px solid var(--color-border);
  text-decoration: none;
}

.mobile-menu-actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 16px;
}

@media (max-width: 768px) {
  .desktop-only {
    display: none;
  }

  .mobile-only {
    display: flex;
  }
}
</style>
