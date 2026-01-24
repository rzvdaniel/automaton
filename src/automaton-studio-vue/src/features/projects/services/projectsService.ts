/**
 * Projects API Service
 * Handles all HTTP calls to projects endpoints
 */

import { useToast } from 'primevue/usetoast';
import apiClient from '@/shared/services/api/axios-instance'
import type {
  ProjectDto,
  ProjectListItem,
  ProjectMemberDto,
  ProjectRunnerDto,
  ProjectAppDto,
  CreateProjectRequest,
  UpdateProjectRequest,
  AddProjectMemberRequest,
  UpdateProjectMemberRequest,
} from '../types';

class ProjectsService {
  private getToast() {
    return useToast();
  }

  /**
   * List all projects the current user is a member of
   */
  async listProjects(): Promise<ProjectListItem[]> {
    try {
      const response = await apiClient.get<ProjectListItem[]>('/api/projects');
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load projects',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Get detailed information about a specific project
   */
  async getProject(projectId: string): Promise<ProjectDto> {
    try {
      const response = await apiClient.get<ProjectDto>(`/api/projects/${projectId}`);
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load project details',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Create a new project
   */
  async createProject(request: CreateProjectRequest): Promise<ProjectDto> {
    try {
      const response = await apiClient.post<ProjectDto>('/api/projects', request);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Project created successfully',
        life: 3000,
      });
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to create project',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Update project details
   */
  async updateProject(projectId: string, request: UpdateProjectRequest): Promise<ProjectDto> {
    try {
      const response = await apiClient.put<ProjectDto>(`/api/projects/${projectId}`, request);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Project updated successfully',
        life: 3000,
      });
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to update project',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Delete (deactivate) a project
   */
  async deleteProject(projectId: string): Promise<void> {
    try {
      await apiClient.delete(`/api/projects/${projectId}`);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Project deleted successfully',
        life: 3000,
      });
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to delete project',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * List all members of a project
   */
  async listMembers(projectId: string): Promise<ProjectMemberDto[]> {
    try {
      const response = await apiClient.get<ProjectMemberDto[]>(
        `/api/projects/${projectId}/members`
      );
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load project members',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Add a new member to the project
   */
  async addMember(projectId: string, request: AddProjectMemberRequest): Promise<ProjectMemberDto> {
    try {
      const response = await apiClient.post<ProjectMemberDto>(
        `/api/projects/${projectId}/members`,
        request
      );
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: `Member invited successfully`,
        life: 3000,
      });
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to invite member',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Update a member's role in the project
   */
  async updateMemberRole(
    projectId: string,
    userId: string,
    request: UpdateProjectMemberRequest
  ): Promise<ProjectMemberDto> {
    try {
      const response = await apiClient.put<ProjectMemberDto>(
        `/api/projects/${projectId}/members/${userId}`,
        request
      );
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Member role updated successfully',
        life: 3000,
      });
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to update member role',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Remove a member from the project
   */
  async removeMember(projectId: string, userId: string): Promise<void> {
    try {
      await apiClient.delete(`/api/projects/${projectId}/members/${userId}`);
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

  /**
   * List all runners assigned to the project
   */
  async listRunners(projectId: string): Promise<ProjectRunnerDto[]> {
    try {
      const response = await apiClient.get<ProjectRunnerDto[]>(
        `/api/projects/${projectId}/runners`
      );
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load project runners',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Add a runner to the project
   */
  async addRunner(projectId: string, runnerId: string): Promise<ProjectRunnerDto> {
    try {
      const response = await apiClient.post<ProjectRunnerDto>(
        `/api/projects/${projectId}/runners/${runnerId}`
      );
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Runner added to project successfully',
        life: 3000,
      });
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to add runner to project',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Remove a runner from the project
   */
  async removeRunner(projectId: string, runnerId: string): Promise<void> {
    try {
      await apiClient.delete(`/api/projects/${projectId}/runners/${runnerId}`);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'Runner removed from project successfully',
        life: 3000,
      });
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to remove runner from project',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * List all apps assigned to the project
   */
  async listApps(projectId: string): Promise<ProjectAppDto[]> {
    try {
      const response = await apiClient.get<ProjectAppDto[]>(
        `/api/projects/${projectId}/apps`
      );
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load project apps',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Add an app to the project
   */
  async addApp(projectId: string, appId: string): Promise<ProjectAppDto> {
    try {
      const response = await apiClient.post<ProjectAppDto>(
        `/api/projects/${projectId}/apps/${appId}`
      );
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'App added to project successfully',
        life: 3000,
      });
      return response.data;
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to add app to project',
        life: 3000,
      });
      throw error;
    }
  }

  /**
   * Remove an app from the project
   */
  async removeApp(projectId: string, appId: string): Promise<void> {
    try {
      await apiClient.delete(`/api/projects/${projectId}/apps/${appId}`);
      this.getToast().add({
        severity: 'success',
        summary: 'Success',
        detail: 'App removed from project successfully',
        life: 3000,
      });
    } catch (error) {
      this.getToast().add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to remove app from project',
        life: 3000,
      });
      throw error;
    }
  }
}

export const projectsService = new ProjectsService();
