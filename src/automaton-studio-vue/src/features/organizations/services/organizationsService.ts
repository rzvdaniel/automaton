/**
 * Organizations API Service
 * Handles all HTTP calls to organizations endpoints
 */

import { useToast } from 'primevue/usetoast';
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
  private getToast() {
    return useToast();
  }

  // =====================================================
  // Organization CRUD
  // =====================================================

  /**
   * List all organizations the current user is a member of
   */
  async listOrganizations(): Promise<OrganizationListItem[]> {
    try {
      const response = await apiClient.get<OrganizationListItem[]>('/api/organizations');
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load organizations',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Get detailed information about a specific organization
   */
  async getOrganization(organizationId: string): Promise<OrganizationDto> {
    try {
      const response = await apiClient.get<OrganizationDto>(`/api/organizations/${organizationId}`);
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load organization details',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Create a new organization
   */
  async createOrganization(request: CreateOrganizationRequest): Promise<OrganizationDto> {
    try {
      const response = await apiClient.post<OrganizationDto>('/api/organizations', request);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Organization created successfully',
        life: 3000,
      });
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to create organization',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Update an existing organization
   */
  async updateOrganization(organizationId: string, request: UpdateOrganizationRequest): Promise<OrganizationDto> {
    try {
      const response = await apiClient.put<OrganizationDto>(`/api/organizations/${organizationId}`, request);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Organization updated successfully',
        life: 3000,
      });
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to update organization',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Delete an organization (soft delete)
   */
  async deleteOrganization(organizationId: string): Promise<void> {
    try {
      await apiClient.delete(`/api/organizations/${organizationId}`);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Organization deleted successfully',
        life: 3000,
      });
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to delete organization',
        life: 3000,
      });
      throw error;
    }
  }

  // =====================================================
  // Membership Management
  // =====================================================

  /**
   * Get all members of an organization
   */
  async getMembers(organizationId: string): Promise<OrganizationMemberDto[]> {
    try {
      const response = await apiClient.get<OrganizationMemberDto[]>(`/api/organizations/${organizationId}/members`);
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load organization members',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Invite a user to join the organization
   */
  async inviteMember(organizationId: string, request: InviteMemberRequest): Promise<any> {
    try {
      const response = await apiClient.post(`/api/organizations/${organizationId}/invitations`, request);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Invitation sent successfully',
        life: 3000,
      });
      return response.data;
    } catch (error: any) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: error.response?.data?.message || 'Failed to send invitation',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Accept an invitation to join an organization
   */
  async acceptInvitation(request: AcceptInvitationRequest): Promise<void> {
    try {
      await apiClient.post('/api/organizations/invitations/accept', request);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Invitation accepted successfully',
        life: 3000,
      });
    } catch (error: any) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: error.response?.data?.message || 'Failed to accept invitation',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Remove a member from an organization
   */
  async removeMember(organizationId: string, userId: string): Promise<void> {
    try {
      await apiClient.delete(`/api/organizations/${organizationId}/members/${userId}`);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Member removed successfully',
        life: 3000,
      });
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to remove member',
        life: 3000,
      });
      throw error;
    }
  }

  // =====================================================
  // App Assignment
  // =====================================================

  /**
   * Get all apps assigned to the current user in an organization
   */
  async getMyAssignedApps(organizationId: string): Promise<any[]> {
    try {
      const response = await apiClient.get(`/api/organizations/${organizationId}/apps/assigned`);
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load assigned apps',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Assign an app to a user within the organization
   */
  async assignApp(organizationId: string, appId: string, request: AssignAppRequest): Promise<any> {
    try {
      const response = await apiClient.post(`/api/organizations/${organizationId}/apps/${appId}/assign`, request);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'App assigned successfully',
        life: 3000,
      });
      return response.data;
    } catch (error: any) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: error.response?.data?.message || 'Failed to assign app',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Unassign an app from a user
   */
  async unassignApp(organizationId: string, appId: string, userId: string): Promise<void> {
    try {
      await apiClient.delete(`/api/organizations/${organizationId}/apps/${appId}/assign/${userId}`);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'App unassigned successfully',
        life: 3000,
      });
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to unassign app',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Get all users assigned to a specific app in the organization
   */
  async getAppAssignedUsers(organizationId: string, appId: string): Promise<any[]> {
    try {
      const response = await apiClient.get(`/api/organizations/${organizationId}/apps/${appId}/users`);
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load assigned users',
        life: 3000,
      });
      throw error;
    }
  }
}

export const organizationsService = new OrganizationsService();
