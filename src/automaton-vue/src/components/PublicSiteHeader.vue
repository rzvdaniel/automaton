<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'

const route = useRoute()
const mobileMenuOpen = ref(false)

type NavLink = {
  label: string
  href: string
  hash?: boolean
  external?: boolean
}

const navLinks: NavLink[] = [
  { label: 'Apps', href: '/apps' },
  { label: 'Docs', href: '/docs' },
  { label: 'Pricing', href: '/pricing' },
]

function isActiveLink(href: string, hash?: boolean, external?: boolean) {
  if (external) {
    return false
  }

  if (hash) {
    return route.path === '/' && route.hash === href.slice(1)
  }

  return route.path === href || route.path.startsWith(`${href}/`)
}
</script>

<template>
  <header class="public-header">
    <div class="shell nav-inner">
      <RouterLink class="brand" to="/">
        <span class="mark">am</span>
        <span>Automaton Monster</span>
      </RouterLink>

      <nav class="nav-links desktop-only" aria-label="Primary navigation">
        <component
          :is="link.hash || link.external ? 'a' : RouterLink"
          v-for="link in navLinks"
          :key="link.label"
          :href="link.hash || link.external ? link.href : undefined"
          :to="link.hash || link.external ? undefined : link.href"
          :target="link.external ? '_blank' : undefined"
          :rel="link.external ? 'noopener' : undefined"
          :class="{ active: isActiveLink(link.href, link.hash, link.external) }"
          :aria-current="isActiveLink(link.href, link.hash, link.external) ? 'page' : undefined"
        >
          {{ link.label }}
        </component>
      </nav>

      <div class="nav-actions desktop-only">
        <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener" class="button primary">Build an app</a>
      </div>

      <button class="mobile-menu-btn mobile-only" type="button" @click="mobileMenuOpen = !mobileMenuOpen">
        <i :class="mobileMenuOpen ? 'pi pi-times' : 'pi pi-bars'" />
      </button>
    </div>

    <div v-if="mobileMenuOpen" class="mobile-menu">
      <component
        :is="link.hash || link.external ? 'a' : RouterLink"
        v-for="link in navLinks"
        :key="link.label"
        :href="link.hash || link.external ? link.href : undefined"
        :to="link.hash || link.external ? undefined : link.href"
        :target="link.external ? '_blank' : undefined"
        :rel="link.external ? 'noopener' : undefined"
        :class="['mobile-nav-link', { active: isActiveLink(link.href, link.hash, link.external) }]"
        :aria-current="isActiveLink(link.href, link.hash, link.external) ? 'page' : undefined"
      >
        {{ link.label }}
      </component>

      <div class="mobile-menu-actions">
        <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener" class="button primary">Build an app</a>
      </div>
    </div>
  </header>
</template>

<style scoped>
.public-header {
  position: sticky;
  top: 0;
  z-index: 120;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  background: rgba(7, 8, 11, 0.78);
  backdrop-filter: blur(18px);
}

.shell {
  width: min(1260px, calc(100% - 40px));
  margin: 0 auto;
}

.nav-inner,
.brand,
.nav-links,
.nav-actions {
  display: flex;
  align-items: center;
}

.nav-inner {
  justify-content: space-between;
  min-height: 68px;
  gap: 24px;
}

.brand {
  gap: 12px;
  color: #f5f7fb;
  font-weight: 800;
  letter-spacing: -0.02em;
  text-decoration: none;
  cursor: pointer;
}

.mark {
  display: grid;
  width: 34px;
  height: 34px;
  place-items: center;
  border: 1px solid rgba(69, 230, 138, 0.48);
  border-radius: 8px;
  background: linear-gradient(135deg, rgba(69, 230, 138, 0.18), rgba(80, 215, 255, 0.12));
  color: #45e68a;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  box-shadow: 0 0 34px rgba(69, 230, 138, 0.14);
}

.nav-links {
  gap: 18px;
}

.nav-links a {
  color: #9ba3b4;
  font-size: 0.94rem;
  text-decoration: none;
  cursor: pointer;
}

.nav-links a:hover,
.nav-links a.active {
  color: #f5f7fb;
}

.nav-actions {
  gap: 10px;
}

.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 40px;
  padding: 0 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.03);
  color: #f5f7fb;
  font-size: 0.92rem;
  font-weight: 700;
  text-decoration: none;
}

.button.primary {
  border-color: rgba(69, 230, 138, 0.58);
  background: #45e68a;
  color: #041008;
  box-shadow: 0 16px 44px rgba(69, 230, 138, 0.18);
}

.desktop-only {
  display: flex;
}

.mobile-only {
  display: none;
}

.mobile-menu-btn {
  padding: 8px;
  border: 0;
  background: transparent;
  color: #f5f7fb;
  font-size: 1.2rem;
  cursor: pointer;
}

.mobile-menu {
  display: grid;
  gap: 4px;
  padding: 0 20px 18px;
  background: rgba(7, 8, 11, 0.95);
}

.mobile-nav-link {
  padding: 12px 4px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  color: #9ba3b4;
  text-decoration: none;
  cursor: pointer;
}

.mobile-nav-link.active {
  color: #f5f7fb;
}

.mobile-menu-actions {
  display: grid;
  gap: 8px;
  margin-top: 12px;
}

@media (max-width: 980px) {
  .desktop-only {
    display: none;
  }

  .mobile-only {
    display: flex;
  }
}

@media (max-width: 640px) {
  .shell {
    width: min(100% - 28px, 1180px);
  }
}
</style>
