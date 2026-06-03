<script setup lang="ts">
import AppFooter from '@/components/AppFooter.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
  <div class="pricing-page">
    <PublicSiteHeader />

    <main class="pricing-main">
      <section class="hero">
        <div class="shell hero-inner">
          <span class="eyebrow">Preview Access</span>
          <h1>Build now. Keep AI and runtime costs under your control.</h1>
          <p>
            Automaton Monster is currently in preview. Frontend apps can be hosted by Automaton
            during preview. Backend apps run on Runner containers you control. Managed runners and
            paid plans will come later.
          </p>
        </div>
      </section>

      <section class="plans">
        <div class="shell plan-grid">
          <article class="plan-card plan-card-primary">
            <div class="plan-top">
              <div>
                <span class="plan-badge">Preview</span>
                <h2>Free during preview</h2>
              </div>
              <div class="plan-price">$0</div>
            </div>

            <p class="plan-copy">
              Build, deploy, publish, fork, and share frontend apps while Automaton Monster is in
              preview.
            </p>

            <ul class="feature-list">
              <li>Build and publish frontend apps</li>
              <li>Public frontend app hosting during preview</li>
              <li>Fork public apps</li>
              <li>CLI + Studio access</li>
              <li>Bring your own AI tools and providers</li>
              <li>Use your own Runner containers for backend apps</li>
              <li>No bundled AI credits or hidden token packages</li>
            </ul>

            <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener" class="button primary">
              Start building
            </a>
          </article>

          <article class="plan-card">
            <div class="plan-top">
              <div>
                <span class="plan-badge plan-badge-soon">Coming later</span>
                <h2>Managed Runners</h2>
              </div>
              <div class="plan-price muted">Paid</div>
            </div>

            <p class="plan-copy">
              Hosted backend execution for developers and teams that want backend apps without
              managing Runner infrastructure themselves.
            </p>

            <ul class="feature-list">
              <li>Managed backend execution</li>
              <li>Hosted Runner provisioning</li>
              <li>Simpler backend deployment</li>
              <li>Runtime options for production apps</li>
              <li>Less infrastructure to manage yourself</li>
            </ul>

            <a href="mailto:hello@automaton.monster?subject=Managed%20Runners%20waitlist" class="button">
              Join waitlist
            </a>
          </article>
        </div>
      </section>

      <section class="explainers">
        <div class="shell explainer-grid">
          <article class="info-card">
            <h2>Why bring your own AI?</h2>
            <p>
              Automaton is designed to work with the AI coding tools you already use. That keeps AI
              usage visible, lets you choose the models you trust, and avoids bundling hidden AI
              subscriptions into the platform.
            </p>
            <ul class="feature-list compact">
              <li>Use Cursor, Claude Code, Codex, Copilot, or another AI tool</li>
              <li>Keep control over AI usage and cost</li>
              <li>No opaque token bundles</li>
              <li>Fits local agentic coding workflows</li>
            </ul>
          </article>

          <article class="info-card">
            <h2>Backend apps today</h2>
            <p>
              Frontend apps are the simplest path and can be published immediately. Backend apps work
              today too, but they run through self-hosted Runners that you control.
            </p>
            <ul class="feature-list compact">
              <li>Frontend apps: hosted by Automaton during preview</li>
              <li>Backend apps: run through your own Runner</li>
              <li>Good fit for developers who want runtime control</li>
            </ul>
          </article>
        </div>
      </section>
    </main>

    <AppFooter />
  </div>
</template>

<style scoped>
.pricing-page {
  min-height: 100vh;
  background:
    radial-gradient(circle at 24% 0%, rgba(80, 215, 255, 0.12), transparent 30rem),
    radial-gradient(circle at 86% 10%, rgba(69, 230, 138, 0.1), transparent 28rem),
    linear-gradient(180deg, #090b10 0%, #07080b 44%, #050608 100%);
  color: #f5f7fb;
}

.shell {
  width: min(1260px, calc(100% - 40px));
  margin: 0 auto;
}

.pricing-main {
  padding: 34px 0 84px;
}

.hero {
  padding: 24px 0 18px;
}

.hero-inner {
  max-width: 900px;
}

.eyebrow {
  display: inline-flex;
  padding: 5px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.78rem;
  font-weight: 850;
  text-transform: uppercase;
}

.hero h1 {
  margin: 16px 0 12px;
  font-size: clamp(2.3rem, 5vw, 4.8rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
}

.hero p {
  max-width: 760px;
  margin: 0;
  color: #c8ceda;
  font-size: 1.06rem;
  line-height: 1.65;
}

.plans,
.explainers {
  padding-top: 22px;
}

.plan-grid,
.explainer-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.plan-card,
.info-card {
  padding: 22px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.26);
}

.plan-card-primary {
  background:
    linear-gradient(135deg, rgba(69, 230, 138, 0.08), rgba(80, 215, 255, 0.04)),
    rgba(255, 255, 255, 0.04);
}

.plan-top {
  display: flex;
  align-items: start;
  justify-content: space-between;
  gap: 18px;
}

.plan-badge {
  display: inline-flex;
  margin-bottom: 10px;
  padding: 4px 9px;
  border-radius: 999px;
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
  font-size: 0.74rem;
  font-weight: 800;
  text-transform: uppercase;
}

.plan-badge-soon {
  background: rgba(255, 203, 87, 0.12);
  color: #ffcb57;
}

.plan-top h2,
.info-card h2 {
  margin: 0;
  font-size: 1.4rem;
  letter-spacing: -0.03em;
}

.plan-price {
  font-size: 2rem;
  font-weight: 800;
  letter-spacing: -0.05em;
}

.plan-price.muted {
  color: #9ba3b4;
}

.plan-copy,
.info-card p {
  margin: 14px 0 0;
  color: #c8ceda;
  line-height: 1.65;
}

.feature-list {
  margin: 18px 0 22px;
  padding-left: 1.2rem;
  color: #f5f7fb;
}

.feature-list li + li {
  margin-top: 0.6rem;
}

.feature-list.compact {
  margin-bottom: 0;
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

@media (max-width: 960px) {
  .plan-grid,
  .explainer-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .shell {
    width: min(100% - 28px, 1180px);
  }

  .pricing-main {
    padding-top: 24px;
  }
}
</style>
