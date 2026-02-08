<template>
  <div class="downloads-page">
    <!-- Page Header -->
    <div class="page-header">
      <h1>Get Started</h1>
      <p>Deploy an Automaton Runner to execute your automation flows</p>
    </div>

    <!-- Installation Method Selector -->
    <div class="method-selector">
      <div
        class="method-card"
        :class="{ active: selectedMethod === 'docker' }"
        @click="selectedMethod = 'docker'"
      >
        <div class="method-icon docker">
          <i class="pi pi-box"></i>
        </div>
        <div class="method-info">
          <h3>Docker</h3>
          <p>Recommended for most users</p>
        </div>
        <Tag value="Recommended" severity="success" class="method-tag" />
      </div>

      <div
        class="method-card"
        :class="{ active: selectedMethod === 'binary' }"
        @click="selectedMethod = 'binary'"
      >
        <div class="method-icon binary">
          <i class="pi pi-download"></i>
        </div>
        <div class="method-info">
          <h3>Binary</h3>
          <p>Direct download for any platform</p>
        </div>
      </div>

      <div
        class="method-card"
        :class="{ active: selectedMethod === 'service' }"
        @click="selectedMethod = 'service'"
      >
        <div class="method-icon service">
          <i class="pi pi-server"></i>
        </div>
        <div class="method-info">
          <h3>System Service</h3>
          <p>Production deployments</p>
        </div>
      </div>
    </div>

    <!-- Docker Installation -->
    <div v-if="selectedMethod === 'docker'" class="installation-content">
      <div class="content-header">
        <h2>Docker Installation</h2>
        <p>Get up and running in seconds with a single command</p>
      </div>

      <!-- Prerequisites -->
      <div class="prereq-banner">
        <i class="pi pi-info-circle"></i>
        <span>
          Requires Docker.
          <a href="https://www.docker.com/products/docker-desktop/" target="_blank" rel="noopener">
            Download Docker Desktop →
          </a>
        </span>
      </div>

      <!-- Quick Start -->
      <div class="quick-start">
        <div class="step-indicator">
          <span class="step-number">1</span>
          <span class="step-label">Get your API Key</span>
        </div>
        <p class="step-description">
          Create an API key from the <RouterLink to="/settings">Settings page</RouterLink> with Runner scope
        </p>
      </div>

      <div class="quick-start">
        <div class="step-indicator">
          <span class="step-number">2</span>
          <span class="step-label">Run this command</span>
        </div>
        <div class="code-container">
          <div class="code-header">
            <span class="code-lang">Terminal</span>
            <Button
              icon="pi pi-copy"
              label="Copy"
              text
              size="small"
              @click="copyToClipboard(dockerRunCommand, 'Docker command')"
            />
          </div>
          <pre class="code-content"><code>{{ dockerRunCommand }}</code></pre>
        </div>
      </div>

      <div class="quick-start">
        <div class="step-indicator">
          <span class="step-number">3</span>
          <span class="step-label">Verify it's running</span>
        </div>
        <div class="code-container">
          <div class="code-header">
            <span class="code-lang">Terminal</span>
            <Button
              icon="pi pi-copy"
              label="Copy"
              text
              size="small"
              @click="copyToClipboard('docker logs -f automaton-runner', 'Logs command')"
            />
          </div>
          <pre class="code-content"><code>docker logs -f automaton-runner</code></pre>
        </div>
      </div>

      <!-- Environment Variables -->
      <Accordion class="env-accordion">
        <AccordionPanel value="0">
          <AccordionHeader>Environment Variables</AccordionHeader>
          <AccordionContent>
            <div class="env-table">
              <div class="env-row">
                <code class="env-name">AUTOMATON_API_KEY</code>
                <span class="env-required">Required</span>
                <span class="env-desc">Your API key from Settings</span>
              </div>
              <div class="env-row">
                <code class="env-name">AUTOMATON_RUNNER_NAME</code>
                <span class="env-optional">Optional</span>
                <span class="env-desc">Display name for this runner</span>
              </div>
              <div class="env-row">
                <code class="env-name">AUTOMATON_SERVER_URL</code>
                <span class="env-optional">Optional</span>
                <span class="env-desc">Custom server URL (auto-configured)</span>
              </div>
            </div>
          </AccordionContent>
        </AccordionPanel>
        <AccordionPanel value="1">
          <AccordionHeader>Docker Compose</AccordionHeader>
          <AccordionContent>
            <div class="code-container">
              <div class="code-header">
                <span class="code-lang">docker-compose.yml</span>
                <Button
                  icon="pi pi-copy"
                  label="Copy"
                  text
                  size="small"
                  @click="copyToClipboard(dockerComposeContent, 'Docker Compose')"
                />
              </div>
              <pre class="code-content"><code>{{ dockerComposeContent }}</code></pre>
            </div>
          </AccordionContent>
        </AccordionPanel>
      </Accordion>
    </div>

    <!-- Binary Installation -->
    <div v-if="selectedMethod === 'binary'" class="installation-content">
      <div class="content-header">
        <h2>Download Binary</h2>
        <p>Choose your platform and architecture</p>
      </div>

      <!-- Platform Selector -->
      <div class="platform-tabs">
        <div
          v-for="platform in platforms"
          :key="platform.id"
          class="platform-tab"
          :class="{ active: selectedPlatform === platform.id }"
          @click="selectedPlatform = platform.id as 'windows' | 'linux' | 'macos'"
        >
          <i :class="platform.icon"></i>
          <span>{{ platform.name }}</span>
        </div>
      </div>

      <!-- Download Options -->
      <div class="download-options">
        <div v-for="arch in currentPlatformDownloads" :key="arch.label" class="download-card">
          <div class="download-info">
            <h4>{{ arch.label }}</h4>
            <p>{{ arch.size }}</p>
          </div>
          <Button
            :label="arch.filename"
            icon="pi pi-download"
            severity="secondary"
            @click="downloadFile(arch.url, arch.filename)"
          />
        </div>
      </div>

      <!-- Quick Install Script -->
      <div class="quick-install-section">
        <h3>Or use the install script</h3>
        <div class="script-options">
          <div v-if="selectedPlatform === 'windows'" class="script-card">
            <div class="script-header">
              <i class="pi pi-window-maximize"></i>
              <span>PowerShell (Admin)</span>
            </div>
            <div class="code-container">
              <div class="code-header">
                <span class="code-lang">PowerShell</span>
                <Button
                  icon="pi pi-copy"
                  label="Copy"
                  text
                  size="small"
                  @click="copyToClipboard(windowsInstallCommand, 'Install script')"
                />
              </div>
              <pre class="code-content"><code>{{ windowsInstallCommand }}</code></pre>
            </div>
          </div>
          <div v-else class="script-card">
            <div class="script-header">
              <i class="pi pi-terminal"></i>
              <span>Terminal</span>
            </div>
            <div class="code-container">
              <div class="code-header">
                <span class="code-lang">Bash</span>
                <Button
                  icon="pi pi-copy"
                  label="Copy"
                  text
                  size="small"
                  @click="copyToClipboard(unixInstallCommand, 'Install script')"
                />
              </div>
              <pre class="code-content"><code>{{ unixInstallCommand }}</code></pre>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- System Service Installation -->
    <div v-if="selectedMethod === 'service'" class="installation-content">
      <div class="content-header">
        <h2>System Service</h2>
        <p>Configure the runner to start automatically on boot</p>
      </div>

      <Message severity="warn" :closable="false" class="prereq-message">
        Run <code>automaton-runner</code> manually first to complete initial configuration
      </Message>

      <!-- Service Platform Tabs -->
      <div class="platform-tabs">
        <div
          class="platform-tab"
          :class="{ active: servicePlatform === 'windows' }"
          @click="servicePlatform = 'windows'"
        >
          <i class="pi pi-window-maximize"></i>
          <span>Windows</span>
        </div>
        <div
          class="platform-tab"
          :class="{ active: servicePlatform === 'linux' }"
          @click="servicePlatform = 'linux'"
        >
          <i class="pi pi-server"></i>
          <span>Linux</span>
        </div>
        <div
          class="platform-tab"
          :class="{ active: servicePlatform === 'macos' }"
          @click="servicePlatform = 'macos'"
        >
          <i class="pi pi-apple"></i>
          <span>macOS</span>
        </div>
      </div>

      <!-- Windows Service -->
      <div v-if="servicePlatform === 'windows'" class="service-content">
        <p class="service-desc">Install as Windows service using NSSM:</p>
        <div class="code-container">
          <div class="code-header">
            <span class="code-lang">PowerShell (Admin)</span>
            <Button
              icon="pi pi-copy"
              label="Copy"
              text
              size="small"
              @click="copyToClipboard(windowsServiceCommand, 'Service command')"
            />
          </div>
          <pre class="code-content"><code>{{ windowsServiceCommand }}</code></pre>
        </div>
      </div>

      <!-- Linux Service -->
      <div v-if="servicePlatform === 'linux'" class="service-content">
        <p class="service-desc">Install as systemd service:</p>
        <div class="code-container">
          <div class="code-header">
            <span class="code-lang">Bash</span>
            <Button
              icon="pi pi-copy"
              label="Copy"
              text
              size="small"
              @click="copyToClipboard(unixServiceCommand, 'Service command')"
            />
          </div>
          <pre class="code-content"><code>{{ unixServiceCommand }}</code></pre>
        </div>
      </div>

      <!-- macOS Service -->
      <div v-if="servicePlatform === 'macos'" class="service-content">
        <p class="service-desc">Install as launchd daemon:</p>
        <div class="code-container">
          <div class="code-header">
            <span class="code-lang">Terminal</span>
            <Button
              icon="pi pi-copy"
              label="Copy"
              text
              size="small"
              @click="copyToClipboard(unixServiceCommand, 'Service command')"
            />
          </div>
          <pre class="code-content"><code>{{ unixServiceCommand }}</code></pre>
        </div>
      </div>
    </div>

    <!-- Footer Actions -->
    <div class="footer-actions">
      <Card class="next-steps-card">
        <template #content>
          <div class="next-steps">
            <div class="next-steps-text">
              <h3>What's next?</h3>
              <p>Monitor your runners and manage API keys</p>
            </div>
            <div class="next-steps-buttons">
              <RouterLink to="/runners" class="no-underline">
                <Button label="View Runners" icon="pi pi-arrow-right" icon-pos="right" />
              </RouterLink>
              <RouterLink to="/settings" class="no-underline">
                <Button label="API Keys" severity="secondary" />
              </RouterLink>
            </div>
          </div>
        </template>
      </Card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Message from 'primevue/message'
import Accordion from 'primevue/accordion'
import AccordionPanel from 'primevue/accordionpanel'
import AccordionHeader from 'primevue/accordionheader'
import AccordionContent from 'primevue/accordioncontent'

const toast = useToast()

// State
const selectedMethod = ref<'docker' | 'binary' | 'service'>('docker')
const selectedPlatform = ref<'windows' | 'linux' | 'macos'>('windows')
const servicePlatform = ref<'windows' | 'linux' | 'macos'>('windows')

// Platform definitions
const platforms = [
  { id: 'windows', name: 'Windows', icon: 'pi pi-window-maximize' },
  { id: 'linux', name: 'Linux', icon: 'pi pi-server' },
  { id: 'macos', name: 'macOS', icon: 'pi pi-apple' },
]

// URLs
const runnerWindowsX64Url = ref('')
const runnerWindowsArm64Url = ref('')
const runnerLinuxX64Url = ref('')
const runnerLinuxArm64Url = ref('')
const runnerMacosX64Url = ref('')
const runnerMacosArm64Url = ref('')
const windowsInstallCommand = ref('')
const unixInstallCommand = ref('')
const windowsServiceCommand = ref('')
const unixServiceCommand = ref('')

// Configuration
const dockerRunCommand = 'docker run --name automaton-runner -d \\\n  --restart unless-stopped \\\n  -e AUTOMATON_API_KEY=your_api_key \\\n  -e AUTOMATON_RUNNER_NAME=my-runner \\\n  rzvdaniel/automaton-runner:latest'

const dockerComposeContent = `services:
  automaton-runner:
    image: rzvdaniel/automaton-runner:latest
    container_name: automaton-runner
    restart: unless-stopped
    environment:
      - AUTOMATON_API_KEY=\${AUTOMATON_API_KEY}
      - AUTOMATON_RUNNER_NAME=my-runner`

// Download options per platform
const currentPlatformDownloads = computed(() => {
  switch (selectedPlatform.value) {
    case 'windows':
      return [
        { label: 'x64 (64-bit)', size: '~30 MB', filename: 'automaton-runner-win-x64.zip', url: runnerWindowsX64Url.value },
        { label: 'ARM64', size: '~30 MB', filename: 'automaton-runner-win-arm64.zip', url: runnerWindowsArm64Url.value },
      ]
    case 'linux':
      return [
        { label: 'x64 (64-bit)', size: '~25 MB', filename: 'automaton-runner-linux-x64.tar.gz', url: runnerLinuxX64Url.value },
        { label: 'ARM64', size: '~25 MB', filename: 'automaton-runner-linux-arm64.tar.gz', url: runnerLinuxArm64Url.value },
      ]
    case 'macos':
      return [
        { label: 'Intel', size: '~25 MB', filename: 'automaton-runner-macos-x64.tar.gz', url: runnerMacosX64Url.value },
        { label: 'Apple Silicon', size: '~25 MB', filename: 'automaton-runner-macos-arm64.tar.gz', url: runnerMacosArm64Url.value },
      ]
    default:
      return []
  }
})

// Initialize URLs
onMounted(() => {
  const baseUrl = window.location.origin

  windowsInstallCommand.value = `iwr -useb ${baseUrl}/downloads/runner/install.ps1 | iex`
  unixInstallCommand.value = `curl -sSL ${baseUrl}/downloads/runner/install.sh | bash`
  windowsServiceCommand.value = `iwr -useb ${baseUrl}/downloads/runner/setup-service.ps1 | iex`
  unixServiceCommand.value = `curl -sSL ${baseUrl}/downloads/runner/setup-service.sh | bash`

  runnerWindowsX64Url.value = `${baseUrl}/downloads/runner/latest/automaton-runner-win-x64.zip`
  runnerWindowsArm64Url.value = `${baseUrl}/downloads/runner/latest/automaton-runner-win-arm64.zip`
  runnerLinuxX64Url.value = `${baseUrl}/downloads/runner/latest/automaton-runner-linux-x64.tar.gz`
  runnerLinuxArm64Url.value = `${baseUrl}/downloads/runner/latest/automaton-runner-linux-arm64.tar.gz`
  runnerMacosX64Url.value = `${baseUrl}/downloads/runner/latest/automaton-runner-macos-x64.tar.gz`
  runnerMacosArm64Url.value = `${baseUrl}/downloads/runner/latest/automaton-runner-macos-arm64.tar.gz`
})

// Copy to clipboard
const copyToClipboard = async (text: string, description: string) => {
  try {
    await navigator.clipboard.writeText(text)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: `${description} copied to clipboard`,
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'warn',
      summary: 'Copy failed',
      detail: 'Please copy manually',
      life: 2000,
    })
  }
}

// Download file
const downloadFile = (url: string, filename: string) => {
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.click()
}
</script>

<style scoped>
.downloads-page {
  max-width: 900px;
  margin: 0 auto;
}

/* Page Header */
.page-header {
  margin-bottom: 2.5rem;
}

.page-header h1 {
  font-size: 2rem;
  font-weight: 700;
  margin: 0 0 0.5rem 0;
  color: var(--p-text-color);
}

.page-header p {
  font-size: 1rem;
  color: var(--p-text-muted-color);
  margin: 0;
}

/* Method Selector */
.method-selector {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1rem;
  margin-bottom: 2rem;
}

.method-card {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 1.5rem 1rem;
  border-radius: 12px;
  border: 2px solid var(--p-surface-200);
  background: var(--p-surface-0);
  cursor: pointer;
  transition: all 0.2s ease;
  text-align: center;
}

.method-card:hover {
  border-color: var(--p-primary-300);
  background: var(--p-surface-50);
}

.method-card.active {
  border-color: var(--p-primary-500);
  background: var(--p-primary-50);
}

.method-icon {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.5rem;
  margin-bottom: 0.75rem;
}

.method-icon.docker {
  background: linear-gradient(135deg, #2496ED 0%, #0B6ECF 100%);
  color: white;
}

.method-icon.binary {
  background: linear-gradient(135deg, #10B981 0%, #059669 100%);
  color: white;
}

.method-icon.service {
  background: linear-gradient(135deg, #8B5CF6 0%, #7C3AED 100%);
  color: white;
}

.method-info h3 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 0.25rem 0;
  color: var(--p-text-color);
}

.method-info p {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  margin: 0;
}

.method-tag {
  position: absolute;
  top: -8px;
  right: -8px;
}

/* Installation Content */
.installation-content {
  animation: fadeIn 0.2s ease;
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateY(8px); }
  to { opacity: 1; transform: translateY(0); }
}

.content-header {
  margin-bottom: 1.5rem;
}

.content-header h2 {
  font-size: 1.5rem;
  font-weight: 600;
  margin: 0 0 0.25rem 0;
  color: var(--p-text-color);
}

.content-header p {
  font-size: 0.875rem;
  color: var(--p-text-muted-color);
  margin: 0;
}

/* Prerequisites Banner */
.prereq-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.875rem 1rem;
  background: var(--p-blue-50);
  border-radius: 8px;
  margin-bottom: 1.5rem;
  font-size: 0.875rem;
  color: var(--p-blue-700);
}

.prereq-banner i {
  font-size: 1rem;
}

.prereq-banner a {
  color: var(--p-blue-600);
  font-weight: 500;
  text-decoration: none;
}

.prereq-banner a:hover {
  text-decoration: underline;
}

/* Quick Start Steps */
.quick-start {
  margin-bottom: 1.5rem;
}

.step-indicator {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.step-number {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: var(--p-primary-500);
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.875rem;
  font-weight: 600;
}

.step-label {
  font-size: 1rem;
  font-weight: 600;
  color: var(--p-text-color);
}

.step-description {
  margin: 0 0 0.75rem 0;
  padding-left: 2.5rem;
  font-size: 0.875rem;
  color: var(--p-text-muted-color);
}

.step-description a {
  color: var(--p-primary-600);
  font-weight: 500;
  text-decoration: none;
}

.step-description a:hover {
  text-decoration: underline;
}

/* Code Container */
.code-container {
  border-radius: 8px;
  overflow: hidden;
  border: 1px solid var(--p-surface-200);
  margin-left: 2.5rem;
}

.code-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem 0.75rem;
  background: var(--p-surface-100);
  border-bottom: 1px solid var(--p-surface-200);
}

.code-lang {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.code-content {
  margin: 0;
  padding: 1rem;
  background: var(--p-surface-900);
  overflow-x: auto;
}

.code-content code {
  font-family: 'SF Mono', 'Consolas', 'Monaco', monospace;
  font-size: 0.8125rem;
  line-height: 1.6;
  color: #e2e8f0;
  white-space: pre-wrap;
  word-break: break-all;
}

/* Accordion */
.env-accordion {
  margin-top: 2rem;
  margin-left: 2.5rem;
}

.env-table {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.env-row {
  display: grid;
  grid-template-columns: 200px 80px 1fr;
  gap: 1rem;
  align-items: center;
  padding: 0.75rem;
  background: var(--p-surface-50);
  border-radius: 6px;
}

.env-name {
  font-family: 'SF Mono', 'Consolas', monospace;
  font-size: 0.8125rem;
  color: var(--p-primary-600);
  font-weight: 500;
}

.env-required {
  font-size: 0.6875rem;
  font-weight: 600;
  text-transform: uppercase;
  color: var(--p-red-600);
}

.env-optional {
  font-size: 0.6875rem;
  font-weight: 600;
  text-transform: uppercase;
  color: var(--p-text-muted-color);
}

.env-desc {
  font-size: 0.8125rem;
  color: var(--p-text-muted-color);
}

/* Platform Tabs */
.platform-tabs {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1.5rem;
  padding: 0.25rem;
  background: var(--p-surface-100);
  border-radius: 8px;
  width: fit-content;
}

.platform-tab {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.625rem 1rem;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--p-text-muted-color);
  transition: all 0.15s ease;
}

.platform-tab:hover {
  color: var(--p-text-color);
}

.platform-tab.active {
  background: var(--p-surface-0);
  color: var(--p-text-color);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
}

/* Download Options */
.download-options {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 1rem;
  margin-bottom: 2rem;
}

.download-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-radius: 8px;
  border: 1px solid var(--p-surface-200);
  background: var(--p-surface-0);
}

.download-info h4 {
  font-size: 0.9375rem;
  font-weight: 600;
  margin: 0 0 0.25rem 0;
  color: var(--p-text-color);
}

.download-info p {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  margin: 0;
}

/* Quick Install Section */
.quick-install-section {
  margin-top: 2rem;
  padding-top: 2rem;
  border-top: 1px solid var(--p-surface-200);
}

.quick-install-section h3 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 1rem 0;
  color: var(--p-text-color);
}

.script-card {
  border-radius: 8px;
  border: 1px solid var(--p-surface-200);
  overflow: hidden;
}

.script-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  background: var(--p-surface-50);
  border-bottom: 1px solid var(--p-surface-200);
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--p-text-color);
}

.script-card .code-container {
  margin-left: 0;
  border: none;
  border-radius: 0;
}

/* Service Content */
.service-content {
  margin-top: 1.5rem;
}

.service-desc {
  font-size: 0.875rem;
  color: var(--p-text-muted-color);
  margin: 0 0 1rem 0;
}

.service-content .code-container {
  margin-left: 0;
}

.prereq-message {
  margin-bottom: 1.5rem;
}

.prereq-message code {
  background: var(--p-surface-100);
  padding: 0.125rem 0.375rem;
  border-radius: 4px;
  font-family: 'SF Mono', 'Consolas', monospace;
  font-size: 0.8125rem;
}

/* Footer Actions */
.footer-actions {
  margin-top: 3rem;
  padding-top: 2rem;
  border-top: 1px solid var(--p-surface-200);
}

.next-steps-card {
  background: linear-gradient(135deg, var(--p-primary-50) 0%, var(--p-surface-0) 100%);
  border: 1px solid var(--p-primary-100);
}

.next-steps {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 2rem;
}

.next-steps-text h3 {
  font-size: 1.125rem;
  font-weight: 600;
  margin: 0 0 0.25rem 0;
  color: var(--p-text-color);
}

.next-steps-text p {
  font-size: 0.875rem;
  color: var(--p-text-muted-color);
  margin: 0;
}

.next-steps-buttons {
  display: flex;
  gap: 0.75rem;
}

.no-underline {
  text-decoration: none;
}

/* Responsive */
@media (max-width: 768px) {
  .method-selector {
    grid-template-columns: 1fr;
  }

  .method-card {
    flex-direction: row;
    text-align: left;
    gap: 1rem;
  }

  .method-icon {
    margin-bottom: 0;
  }

  .method-tag {
    position: static;
    margin-left: auto;
  }

  .download-options {
    grid-template-columns: 1fr;
  }

  .code-container {
    margin-left: 0;
  }

  .step-description {
    padding-left: 0;
  }

  .env-accordion {
    margin-left: 0;
  }

  .env-row {
    grid-template-columns: 1fr;
    gap: 0.25rem;
  }

  .next-steps {
    flex-direction: column;
    text-align: center;
  }
}

/* Dark mode */
:global(.app-dark) .method-card {
  background: var(--p-surface-50);
  border-color: var(--p-surface-200);
}

:global(.app-dark) .method-card:hover {
  background: var(--p-surface-100);
}

:global(.app-dark) .method-card.active {
  background: var(--p-primary-900);
  border-color: var(--p-primary-500);
}

:global(.app-dark) .prereq-banner {
  background: var(--p-blue-900);
  color: var(--p-blue-200);
}

:global(.app-dark) .prereq-banner a {
  color: var(--p-blue-300);
}

:global(.app-dark) .code-header {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .code-container {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .env-row {
  background: var(--p-surface-800);
}

:global(.app-dark) .platform-tabs {
  background: var(--p-surface-800);
}

:global(.app-dark) .platform-tab.active {
  background: var(--p-surface-600);
}

:global(.app-dark) .download-card {
  background: var(--p-surface-50);
  border-color: var(--p-surface-200);
}

:global(.app-dark) .script-header {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .script-card {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .next-steps-card {
  background: linear-gradient(135deg, var(--p-primary-900) 0%, var(--p-surface-50) 100%);
  border-color: var(--p-primary-800);
}
</style>
