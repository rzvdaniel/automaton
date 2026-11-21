import apiClient from '@/shared/services/api/axios-instance'

export interface ActionWebhookCall {
  calledAtUtc: string
  outcome: string
  executionId?: string
  detail?: string
}

export interface ActionWebhook {
  id: string
  applicationId: string
  actionKey: string
  name: string
  enabled: boolean
  createdAtUtc: string
  lastCalledAtUtc?: string
  url: string
  calls: ActionWebhookCall[]
  /** Present only in the answer to creating a webhook or rotating its secret. */
  secret?: string
}

const base = (appId: string) => `/api/apps/${appId}/action-webhooks`

export const actionWebhooksService = {
  async list(appId: string): Promise<ActionWebhook[]> {
    return (await apiClient.get(base(appId))).data
  },
  async create(appId: string, actionKey: string, name: string): Promise<ActionWebhook> {
    return (await apiClient.post(base(appId), { actionKey, name })).data
  },
  async setEnabled(appId: string, id: string, enabled: boolean): Promise<ActionWebhook> {
    return (await apiClient.post(`${base(appId)}/${id}/${enabled ? 'enable' : 'disable'}`)).data
  },
  async rotate(appId: string, id: string): Promise<ActionWebhook> {
    return (await apiClient.post(`${base(appId)}/${id}/rotate`)).data
  },
  async remove(appId: string, id: string): Promise<void> {
    await apiClient.delete(`${base(appId)}/${id}`)
  },
}
