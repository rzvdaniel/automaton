// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import InstallAppView from './InstallAppView.vue'
import { appsService } from '../services/apps.service'
import { actionSchedulesService } from '../services/action-schedules.service'
import { adminAppsService } from '@/features/admin/services/adminAppsService'

const routeState = vi.hoisted(() => ({ route: { params: { sourceAppId: 'src-1' } as Record<string, string>, query: {} as Record<string, string> } }))
vi.mock('vue-router', () => ({ useRoute: () => routeState.route, useRouter: () => ({ push: vi.fn() }) }))
vi.mock('@/features/auth/stores/auth.store', () => ({ useAuthStore: () => ({ user: { roles: ['SysAdmin'] } }) }))
vi.mock('../services/apps.service', () => ({
  appsService: {
    getInstallSource: vi.fn(), getGitInstallSource: vi.fn(), installFromGit: vi.fn(), install: vi.fn(), setSecret: vi.fn(), deployStoredSource: vi.fn(), getDeploymentOperation: vi.fn(),
  },
}))
vi.mock('@/features/runners/services/runners.service', () => ({ runnersService: { list: vi.fn().mockResolvedValue([{ id: 'r1', name: 'Runner', status: 'Online', purpose: 'MachineManagement', applicationProfile: 'MachineManagement' }]) } }))
vi.mock('../utils/deployment-target-runner', () => ({ getDeploymentTargetRunnerOptions: () => [{ label: 'Runner', value: 'r1', disabled: false }] }))
vi.mock('../services/action-schedules.service', () => ({ actionSchedulesService: { create: vi.fn() } }))
vi.mock('@/features/admin/services/adminAppsService', () => ({ adminAppsService: { setPermission: vi.fn() } }))

window.matchMedia ??= (() => ({ matches: false, addEventListener: () => {}, removeEventListener: () => {} })) as never

describe('InstallAppView', () => {
  it('installs with settings, secrets, approval and a schedule, in order', async () => {
    vi.mocked(appsService.getInstallSource).mockResolvedValue({
      id: 'src-1', name: 'Off-site Backup', creatorDisplayName: 'Me',
      application: {
        settings: [{ name: 'S3_REGION', default: 'us-east-1' }],
        secrets: [{ name: 'S3_SECRET_ACCESS_KEY' }],
        permissions: ['backups:read'],
        schedules: [{ name: 'Nightly', action: 'upload', cron: '30 3 * * *' }],
      },
    })
    vi.mocked(appsService.install).mockResolvedValue({ id: 'new-1' } as never)
    vi.mocked(appsService.deployStoredSource).mockResolvedValue({ operationId: 'op', status: 'Pending' })
    vi.mocked(appsService.getDeploymentOperation).mockResolvedValue({ operationId: 'op', status: 'Succeeded' })
    const wrapper = mount(InstallAppView, { global: { plugins: [PrimeVue] } })
    await flushPromises()

    const install = () => wrapper.findAll('button').find((b) => b.text().includes('Install'))!
    expect(install().attributes('disabled')).toBeDefined() // secret still missing
    await wrapper.find('#secret-S3_SECRET_ACCESS_KEY').setValue('shh')
    await install().trigger('click')
    await flushPromises()

    expect(appsService.install).toHaveBeenCalledWith('src-1', 'Off-site Backup', 'r1')
    expect(appsService.setSecret).not.toHaveBeenCalledWith('new-1', 'S3_REGION', expect.anything()) // default is not pinned
    expect(appsService.setSecret).toHaveBeenCalledWith('new-1', 'S3_SECRET_ACCESS_KEY', 'shh')
    expect(adminAppsService.setPermission).toHaveBeenCalledWith('new-1', 'backups:read', true)
    expect(actionSchedulesService.create).toHaveBeenCalledWith(expect.objectContaining({
      applicationId: 'new-1', actionKey: 'upload', scheduleType: 'Cron', cronExpression: '30 3 * * *',
    }))
    expect(wrapper.text()).not.toContain('shh')
    expect(wrapper.get('.install-progress').text()).toContain('Installed')
    expect(wrapper.get('.install-progress').text()).toContain('Deploy and start the app')
    expect(wrapper.get('.install-progress').text()).toContain('Nightly runs on cron 30 3 * * *')
    expect(wrapper.text()).toContain('Open the installed app')
  })

  it('shows the form again with the error when nothing was created', async () => {
    vi.mocked(appsService.getInstallSource).mockResolvedValue({ id: 'src-1', name: 'Off-site Backup', creatorDisplayName: 'Me', application: {} })
    vi.mocked(appsService.install).mockRejectedValue(new Error('Name already used'))
    const wrapper = mount(InstallAppView, { global: { plugins: [PrimeVue] } })
    await flushPromises()
    await wrapper.findAll('button').find((b) => b.text().includes('Install'))!.trigger('click')
    await flushPromises()

    expect(wrapper.find('.install-progress').exists()).toBe(false)
    expect(wrapper.text()).toContain('Name already used')
    expect(wrapper.find('#install-name').exists()).toBe(true)
  })

  it('installs from a pinned Git source, shows where it comes from and never calls the store install', async () => {
    const commit = 'a'.repeat(40)
    routeState.route = { params: {}, query: { repo: 'o/apps', folder: 'm/backup', commit } }
    vi.mocked(appsService.getGitInstallSource).mockResolvedValue({
      id: '', name: 'Backup', git: { repo: 'o/apps', folder: 'm/backup', commit }, application: { permissions: ['backups:read'] },
    })
    vi.mocked(appsService.installFromGit).mockResolvedValue({ id: 'new-2' } as never)
    vi.mocked(appsService.deployStoredSource).mockResolvedValue({ operationId: 'op', status: 'Pending' })
    vi.mocked(appsService.getDeploymentOperation).mockResolvedValue({ operationId: 'op', status: 'Succeeded' })
    vi.mocked(appsService.install).mockClear()
    const wrapper = mount(InstallAppView, { global: { plugins: [PrimeVue] } })
    await flushPromises()

    expect(appsService.getGitInstallSource).toHaveBeenCalledWith({ repo: 'o/apps', folder: 'm/backup', commit })
    expect(wrapper.text()).toContain('o/apps')
    expect(wrapper.text()).toContain('a'.repeat(12))
    expect(wrapper.text()).toContain('backups:read')
    await wrapper.findAll('button').find((b) => b.text().includes('Install'))!.trigger('click')
    await flushPromises()

    expect(appsService.installFromGit).toHaveBeenCalledWith({ repo: 'o/apps', folder: 'm/backup', commit }, 'Backup', 'r1')
    expect(appsService.install).not.toHaveBeenCalled()
    expect(adminAppsService.setPermission).toHaveBeenCalledWith('new-2', 'backups:read', true)
    routeState.route = { params: { sourceAppId: 'src-1' }, query: {} }
  })
})
