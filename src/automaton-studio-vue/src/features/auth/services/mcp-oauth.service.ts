import apiClient from '@/shared/services/api/axios-instance'

export interface McpOAuthParams {
  client_id: string
  redirect_uri: string
  code_challenge: string
  code_challenge_method: string
  state?: string
}

export async function describeMcpOAuthRequest(params: McpOAuthParams): Promise<{ clientName: string; redirectHost: string }> {
  const { data } = await apiClient.get<{ clientName: string; redirectHost: string }>('/api/mcp-oauth/request', { params })
  return data
}

/** Returns the address to send the browser to, carrying the one-time code (approve) or the refusal (deny). */
export async function decideMcpOAuthRequest(decision: 'approve' | 'deny', params: McpOAuthParams): Promise<string> {
  const { data } = await apiClient.post<{ redirectUrl: string }>(`/api/mcp-oauth/${decision}`, params)
  return data.redirectUrl
}
