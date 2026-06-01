<script setup lang="ts">
import { publicAppConfig } from '@/config/publicAppConfig'
import logoUrl from '@/assets/logo.svg'
</script>

<template>
  <footer class="site-footer">
    <div class="container footer-inner">
      <div class="footer-brand">
        <div class="footer-logo">
          <img :src="logoUrl" alt="" class="footer-logo-icon" aria-hidden="true" />
          <span>Automaton</span>
        </div>
        <p class="footer-tagline">The fastest way to turn your code into a live app.</p>
        <p class="footer-preview-note">Automaton is currently available in Preview.</p>
        <div class="footer-social">
          <a href="https://github.com" target="_blank" class="social-link" aria-label="GitHub">
            <i class="pi pi-github" />
          </a>
          <a href="https://twitter.com" target="_blank" class="social-link" aria-label="Twitter">
            <i class="pi pi-twitter" />
          </a>
          <a href="https://discord.com" target="_blank" class="social-link" aria-label="Discord">
            <i class="pi pi-discord" />
          </a>
        </div>
      </div>

      <div class="footer-col">
        <h4 class="footer-col-title">Product</h4>
        <a href="/apps" class="footer-link">Apps</a>
        <a href="/cli" class="footer-link">CLI</a>
        <a href="/pricing" class="footer-link">Pricing</a>
        <a href="#" class="footer-link">Changelog</a>
      </div>

      <div class="footer-col">
        <h4 class="footer-col-title">Resources</h4>
        <a href="/docs" class="footer-link">Docs</a>
        <a href="/cli" class="footer-link">CLI Install</a>
        <a :href="publicAppConfig.llmsUrl" target="_blank" rel="noopener" class="footer-link">llms.txt</a>
        <a href="/docs/cli-ai-tools" class="footer-link">Deploy Guide</a>
      </div>

      <div class="footer-col">
        <h4 class="footer-col-title">Company</h4>
        <a href="#" class="footer-link">About</a>
        <a href="mailto:hello@automaton.app" class="footer-link">Contact</a>
        <a href="#" class="footer-link">Terms</a>
        <a href="#" class="footer-link">Privacy</a>
      </div>

      <div class="footer-side">
        <p class="footer-side-copy">Built by a solo developer.</p>
        <a href="mailto:hello@automaton.app" class="footer-cta">Get in touch <i class="pi pi-arrow-right" /></a>
        <div class="footer-copyright">© 2026 Automaton</div>
      </div>
    </div>
  </footer>
</template>

<style scoped>
.site-footer {
  border-top: 1px solid rgba(255, 255, 255, 0.06);
  background: transparent;
}

.footer-inner {
  display: grid;
  grid-template-columns: 1.2fr 0.8fr 0.9fr 0.8fr 1fr;
  gap: 28px;
  padding-top: 28px;
  padding-bottom: 32px;
}

.footer-logo {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  font-size: 1.05rem;
  font-weight: 700;
  color: var(--color-text);
}

.footer-logo-icon {
  width: 28px;
  height: 28px;
  display: block;
}

.footer-tagline {
  max-width: 230px;
  margin-bottom: 18px;
  font-size: 0.86rem;
  line-height: 1.6;
  color: rgba(233, 236, 245, 0.68);
}

.footer-preview-note {
  max-width: 280px;
  margin: 0 0 18px;
  font-size: 0.8rem;
  line-height: 1.6;
  color: rgba(233, 236, 245, 0.56);
}

.footer-social {
  display: flex;
  gap: 8px;
}

.social-link {
  width: 34px;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 10px;
  border: 1px solid var(--color-border);
  background: rgba(255, 255, 255, 0.02);
  color: var(--color-text-muted);
  transition: color 0.2s, border-color 0.2s;
}

.social-link:hover {
  color: var(--color-text);
  border-color: rgba(255, 255, 255, 0.18);
}

.footer-col {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.footer-col-title {
  margin-bottom: 6px;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--color-text);
}

.footer-link {
  font-size: 0.875rem;
  color: rgba(233, 236, 245, 0.68);
  text-decoration: none;
  transition: color 0.2s;
}

.footer-link:hover {
  color: var(--color-text);
}

.footer-side {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 12px;
}

.footer-side-copy,
.footer-copyright {
  font-size: 0.86rem;
  line-height: 1.6;
  color: rgba(233, 236, 245, 0.68);
}

.footer-cta {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  border-radius: 10px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: rgba(255, 255, 255, 0.03);
  color: var(--color-text);
  text-decoration: none;
}

.footer-cta:hover {
  border-color: rgba(255, 255, 255, 0.18);
}

@media (max-width: 980px) {
  .footer-inner {
    grid-template-columns: 1fr 1fr;
  }

  .footer-brand,
  .footer-side {
    grid-column: 1 / -1;
  }
}

@media (max-width: 640px) {
  .footer-inner {
    grid-template-columns: 1fr;
    gap: 24px;
  }
}
</style>
