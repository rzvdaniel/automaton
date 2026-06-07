<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
import logoUrl from '@/assets/logo.svg'

const mobileMenuOpen = ref(false)
const route = useRoute()

const navLinks = [
  { label: 'Apps', href: '/apps' },
  { label: 'CLI', href: '/cli' },
  { label: 'Docs', href: '/docs/', external: true },
  { label: 'GitHub', href: 'https://github.com', external: true },
]

function isHomeActive() {
  return route.path === '/'
}

function isActiveLink(href: string, external?: boolean) {
  if (external) return false
  return route.path === href || route.path.startsWith(`${href}/`)
}
</script>

<template>
  <header class="nav-header">
    <div class="container nav-inner">
      <RouterLink to="/" :class="['nav-logo', isHomeActive() && 'nav-logo-active']" :aria-current="isHomeActive() ? 'page' : undefined">
        <img :src="logoUrl" alt="" class="logo-icon" aria-hidden="true" />
        <span class="logo-text">Automaton</span>
      </RouterLink>

      <nav class="nav-links desktop-only">
        <component
          v-for="link in navLinks"
          :key="link.label"
          :is="link.external ? 'a' : RouterLink"
          :to="link.external ? undefined : link.href"
          :href="link.external ? link.href : undefined"
          :target="link.external ? '_blank' : undefined"
          :class="['nav-link', isActiveLink(link.href, link.external) && 'nav-link-active']"
          :aria-current="isActiveLink(link.href, link.external) ? 'page' : undefined"
        >
          {{ link.label }}
        </component>
      </nav>

      <div class="nav-actions desktop-only">
        <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener" class="btn btn-secondary btn-sm">Sign in</a>
        <RouterLink to="/cli" class="btn btn-primary btn-sm">Get Started</RouterLink>
      </div>

      <button class="mobile-menu-btn mobile-only" @click="mobileMenuOpen = !mobileMenuOpen">
        <i :class="mobileMenuOpen ? 'pi pi-times' : 'pi pi-bars'" />
      </button>
    </div>

    <div v-if="mobileMenuOpen" class="mobile-menu">
      <component
        :is="link.external ? 'a' : RouterLink"
        v-for="link in navLinks"
        :key="link.label"
        :to="link.external ? undefined : link.href"
        :href="link.external ? link.href : undefined"
        :target="link.external ? '_blank' : undefined"
        :class="['mobile-nav-link', isActiveLink(link.href, link.external) && 'mobile-nav-link-active']"
        :aria-current="isActiveLink(link.href, link.external) ? 'page' : undefined"
      >
        {{ link.label }}
      </component>
      <div class="mobile-menu-actions">
        <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener" class="btn btn-secondary" style="width:100%; justify-content:center">Sign in</a>
        <RouterLink to="/cli" class="btn btn-primary" style="width:100%; justify-content:center">Get Started</RouterLink>
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
  position: relative;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px 6px 12px;
  border-radius: var(--radius-sm);
  font-weight: 700;
  font-size: 1.1rem;
  color: var(--color-text);
  text-decoration: none;
  transition: background 0.2s, box-shadow 0.2s;
}

.nav-logo:hover {
  background: var(--color-surface-2);
}

.nav-logo-active {
  background: rgba(124, 106, 245, 0.16);
  box-shadow:
    inset 0 0 0 1px rgba(124, 106, 245, 0.4),
    0 0 0 1px rgba(124, 106, 245, 0.08);
}

.nav-logo-active::after {
  content: '';
  position: absolute;
  left: 12px;
  right: 12px;
  bottom: -8px;
  height: 2px;
  border-radius: 999px;
  background: linear-gradient(90deg, #7c6af5, #4da6ff);
}

.logo-icon {
  width: 28px;
  height: 28px;
  flex: 0 0 28px;
  display: block;
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
  position: relative;
  padding: 6px 14px;
  border-radius: var(--radius-sm);
  color: var(--color-text-muted);
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: color 0.2s, background 0.2s;
  text-decoration: none;
}

.nav-link:hover {
  color: var(--color-text);
  background: var(--color-surface-2);
}

.nav-link-active {
  color: var(--color-text);
  background: rgba(124, 106, 245, 0.16);
  box-shadow:
    inset 0 0 0 1px rgba(124, 106, 245, 0.4),
    0 0 0 1px rgba(124, 106, 245, 0.08);
}

.nav-link-active::after {
  content: '';
  position: absolute;
  left: 12px;
  right: 12px;
  bottom: -8px;
  height: 2px;
  border-radius: 999px;
  background: linear-gradient(90deg, #7c6af5, #4da6ff);
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
  cursor: pointer;
  border-bottom: 1px solid var(--color-border);
  text-decoration: none;
}

.mobile-nav-link-active {
  color: var(--color-text);
  border-bottom-color: rgba(124, 106, 245, 0.38);
  background: rgba(124, 106, 245, 0.08);
  box-shadow: inset 3px 0 0 #7c6af5;
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
