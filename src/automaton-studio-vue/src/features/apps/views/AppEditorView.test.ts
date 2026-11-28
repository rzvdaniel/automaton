// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import AppEditorView from './AppEditorView.vue'
import { appsService } from '../services/apps.service'

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'app-1' }, query: {} }),
  useRouter: () => ({ replace: vi.fn().mockResolvedValue(undefined), push: vi.fn().mockResolvedValue(undefined) }),
}))

vi.mock('../services/apps.service', () => ({
  appsService: {
    get: vi.fn().mockResolvedValue({
      id: 'app-1',
      name: 'Agent Keeper',
      summary: 'Turns a machine into an agent runner.',
      canUpdate: true,
      isPublished: false,
      requiresAuthentication: true,
      installedFromRepo: 'rzvdaniel/automaton-apps',
      installedFromFolder: 'maintenance/agent-keeper',
      installedFromCommit: 'b166c9f',
      developmentMode: 'local',
      runtimeSource: 'cli-deploy',
      application: {
        runtime: { command: { executable: 'node', arguments: [], workingDirectory: '.', environment: {}, useShell: false }, port: 8080 },
        build: { command: null, timeoutSeconds: 900 },
        health: { startupTimeoutSeconds: 60 },
        storage: { dataDirectory: 'data', mounts: [] },
        pages: [{ path: '/', label: 'Agent queue' }],
      },
    }),
    listDeploymentTargets: vi.fn().mockResolvedValue([
      {
        id: 't1',
        appId: 'app-1',
        name: 'Live',
        slug: 'live',
        isDefault: true,
        status: 'Running',
        genericRuntimeDeployed: true,
        lastSuccessfulDeploymentAt: new Date().toISOString(),
        cleanupPending: false,
        routePath: '/',
        runner: { id: 'r1', name: 'Automaton-Runner-01', purpose: 'Managed', isHostedRunner: false, isOnline: true },
      },
    ]),
    listActionExecutions: vi.fn().mockResolvedValue([]),
    getSharing: vi.fn().mockResolvedValue({ audience: 'me', people: [] }),
    listSecrets: vi.fn().mockResolvedValue([]),
    getDisk: vi.fn().mockResolvedValue(null),
    getCatalogUpdate: vi.fn().mockResolvedValue({ available: false }),
    getCatalog: vi.fn().mockResolvedValue({ apps: [] }),
  },
}))

vi.mock('../services/action-schedules.service', () => ({
  actionSchedulesService: {
    listActions: vi.fn().mockResolvedValue([]),
    listSchedules: vi.fn().mockResolvedValue([]),
  },
}))

vi.mock('../services/backend-runtime-log-client', () => ({
  backendRuntimeLogClient: {
    onLog: () => () => {},
    subscribe: vi.fn().mockResolvedValue(undefined),
    unsubscribe: vi.fn().mockResolvedValue(undefined),
  },
}))

vi.mock('@/features/home/services/home.service', () => ({
  homeService: { get: vi.fn().mockResolvedValue({ needsYou: [], pinned: [] }), pin: vi.fn(), unpin: vi.fn() },
}))

vi.mock('@/features/home/services/notifications.service', () => ({
  notificationsService: { list: vi.fn().mockResolvedValue([]) },
}))

vi.mock('@/features/auth/stores/auth.store', () => ({
  useAuthStore: () => ({ user: { roles: ['User'] } }),
}))

describe('AppEditorView header', () => {
  it('shows green healthy status pill, body-font access pill, and uniform secondary buttons beside primary Open app', async () => {
    setActivePinia(createPinia())
    const wrapper = mount(AppEditorView, {
      global: { plugins: [PrimeVue, ToastService, ConfirmationService], stubs: { RouterLink: true } },
    })
    await flushPromises()

    const health = wrapper.get('[data-testid="app-health"]')
    expect(health.text()).toBe('healthy')
    expect(health.classes()).toContain('health--healthy')

    const access = wrapper.get('[data-testid="app-access"]')
    expect(access.text()).toBe('Only you')
    expect(access.classes()).toContain('pill--access')
    expect(access.classes()).not.toContain('pill')

    for (const testId of ['pin-to-home', 'share-app', 'more-actions']) {
      const btn = wrapper.get(`[data-testid="${testId}"]`)
      expect(btn.classes()).toContain('p-button-outlined')
      expect(btn.classes()).toContain('p-button-secondary')
      expect(btn.classes()).not.toContain('p-button-sm')
    }

    const openBtn = wrapper.get('[data-testid="open-app"]')
    expect(openBtn.classes()).toContain('open-app')
    expect(openBtn.classes()).not.toContain('p-button-outlined')
    expect(appsService.get).toHaveBeenCalledWith('app-1')
  })
})
