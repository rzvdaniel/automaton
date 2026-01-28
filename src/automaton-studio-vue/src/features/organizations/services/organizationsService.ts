/**
 * Organizations API Service
 * Handles all HTTP calls to organizations endpoints
 */

import apiClient from '@/shared/services/api/axios-instance'
import type {
  OrganizationDto,
  OrganizationListItem,
  OrganizationMemberDto,
  CreateOrganizationRequest,
  UpdateOrganizationRequest,
  InviteMemberRequest,
  AcceptInvitationRequest,
  AssignAppRequest,
} from '../types';

class OrganizationsService {

  // =====================================================
  // Organization CRUD
  // =====================================================

  /**
   * List all organizations the current user is a member of
   */
  async listOrganizations(): Promise<OrganizationListItem[]> {
    const response = await apiClient.get<OrganizationListItem[]>('/api/organizations');
    return response.data;
  }

  /**
   * Get detailed information about a specific organization
   */
  async getOrganization(organizationId: string): Promise<OrganizationDto> {
    const response = await apiClient.get<OrganizationDto>(`/api/organizations/${organizationId}`);
    return response.data;
  }

  /**
   * Create a new organization
   */
  async createOrganization(request: CreateOrganizationRequest): Promise<OrganizationDto> {
    const response = await apiClient.post<OrganizationDto>('/api/organizations', request);
    return response.data;
  }

  /**
   * Update an existing organization
   */
  async updateOrganization(organizationId: string, request: UpdateOrganizationRequest): Promise<OrganizationDto> {
    const response = await apiClient.put<OrganizationDto>(`/api/organizations/${organizationId}`, request);
    return response.data;
  }

  /**
   * Delete an organization (soft delete)
   */
  async deleteOrganization(organizationId: string): Promise<void> {
    await apiClient.delete(`/api/organizations/${organizationId}`);
  }

  // =====================================================
  // Membership Management
  // =====================================================

  /**
   * Get all members of an organization
   */
  async getMembers(organizationId: string): Promise<OrganizationMemberDto[]> {
    const response = await apiClient.get<OrganizationMemberDto[]>(`/api/organizations/${organizationId}/members`);
    return response.data;
  }

  /**
   * Invite a user to join the organization
   */
  async inviteMember(organizationId: string, request: InviteMemberRequest): Promise<any> {
    const response = await apiClient.post(`/api/organizations/${organizationId}/invitations`, request);
    return response.data;
  }

  /**
   * Accept an invitation to join an organization
   */
  async acceptInvitation(request: AcceptInvitationRequest): Promise<void> {
    await apiClient.post('/api/organizations/invitations/accept', request);
  }

  /**
   * Remove a member from an organization
   */
  async removeMember(organizationId: string, userId: string): Promise<void> {
    await apiClient.delete(`/api/organizations/${organizationId}/members/${userId}`);
  }

  // =====================================================
  // Projects
  // =====================================================

  /**
   * Get all projects in the organization
   */
  async getOrganizationProjects(organizationId: string): Promise<any[]> {
    const response = await apiClient.get(`/api/organizations/${organizationId}/projects`);
    return response.data;
  }

  // =====================================================
  // App Assignment
  // =====================================================

  /**
   * Get all apps from projects in the organization
   */
  async getOrganizationApps(organizationId: string): Promise<any[]> {
    const response = await apiClient.get(`/api/organizations/${organizationId}/apps`);
    return response.data;
  }

  /**
   * Get all apps assigned to the current user in an organization
   */
  async getMyAssignedApps(organizationId: string): Promise<any[]> {
    const response = await apiClient.get(`/api/organizations/${organizationId}/apps/assigned`);
    return response.data;
  }

  /**
   * Assign an app to a user within the organization
   */
  async assignApp(organizationId: string, appId: string, request: AssignAppRequest): Promise<any> {
    const response = await apiClient.post(`/api/organizations/${organizationId}/apps/${appId}/assign`, request);
    return response.data;
  }

  /**
   * Unassign an app from a user
   */
  async unassignApp(organizationId: string, appId: string, userId: string): Promise<void> {
    await apiClient.delete(`/api/organizations/${organizationId}/apps/${appId}/assign/${userId}`);
  }

  /**
   * Get all users assigned to a specific app in the organization
   */
  async getAppAssignedUsers(organizationId: string, appId: string): Promise<any[]> {
    const response = await apiClient.get(`/api/organizations/${organizationId}/apps/${appId}/users`);
    return response.data;
  }
  
  /**
   * Link an existing project to the organization
   */
  async linkProject(organizationId: string, projectId: string): Promise<void> {
    await apiClient.put(`/api/organizations/${organizationId}/projects/${projectId}`);
  }
  
  /**
   * Unlink a project from the organization
   */
  async unlinkProject(organizationId: string, projectId: string): Promise<void> {
    await apiClient.delete(`/api/organizations/${organizationId}/projects/${projectId}`);
  }
}

export const organizationsService = new OrganizationsService();
