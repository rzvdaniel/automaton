/**
 * Projects API Service
 * Handles all HTTP calls to projects endpoints
 */

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
  /**
   * List all projects the current user is a member of
   */
  async listProjects(): Promise<ProjectListItem[]> {
    const response = await apiClient.get<ProjectListItem[]>('/api/projects');
    return response.data;
  }

  /**
   * Get detailed information about a specific project
   */
  async getProject(projectId: string): Promise<ProjectDto> {
    const response = await apiClient.get<ProjectDto>(`/api/projects/${projectId}`);
    return response.data;
  }

  /**
   * Create a new project
   */
  async createProject(request: CreateProjectRequest): Promise<ProjectDto> {
    const response = await apiClient.post<ProjectDto>('/api/projects', request);
    return response.data;
  }

  /**
   * Update project details
   */
  async updateProject(projectId: string, request: UpdateProjectRequest): Promise<ProjectDto> {
    const response = await apiClient.put<ProjectDto>(`/api/projects/${projectId}`, request);
    return response.data;
  }

  /**
   * Delete (deactivate) a project
   */
  async deleteProject(projectId: string): Promise<void> {
    await apiClient.delete(`/api/projects/${projectId}`);
  }

  /**
   * Reactivate a previously deleted (deactivated) project
   */
  async reactivateProject(projectId: string): Promise<void> {
    await apiClient.post(`/api/projects/${projectId}/reactivate`);
  }

  /**
   * List all members of a project
   */
  async listMembers(projectId: string): Promise<ProjectMemberDto[]> {
    const response = await apiClient.get<ProjectMemberDto[]>(
      `/api/projects/${projectId}/members`
    );
    return response.data;
  }

  /**
   * Add a new member to the project
   */
  async addMember(projectId: string, request: AddProjectMemberRequest): Promise<ProjectMemberDto> {
    const response = await apiClient.post<ProjectMemberDto>(
      `/api/projects/${projectId}/members`,
      request
    );
    return response.data;
  }

  /**
   * Update a member's role in the project
   */
  async updateMemberRole(
    projectId: string,
    userId: string,
    request: UpdateProjectMemberRequest
  ): Promise<ProjectMemberDto> {
    const response = await apiClient.put<ProjectMemberDto>(
      `/api/projects/${projectId}/members/${userId}`,
      request
    );
    return response.data;
  }

  /**
   * Remove a member from the project
   */
  async removeMember(projectId: string, userId: string): Promise<void> {
    await apiClient.delete(`/api/projects/${projectId}/members/${userId}`);
  }

  /**
   * List all runners assigned to the project
   */
  async listRunners(projectId: string): Promise<ProjectRunnerDto[]> {
    const response = await apiClient.get<ProjectRunnerDto[]>(
      `/api/projects/${projectId}/runners`
    );
    return response.data;
  }

  /**
   * Add a runner to the project
   */
  async addRunner(projectId: string, runnerId: string): Promise<ProjectRunnerDto> {
    const response = await apiClient.post<ProjectRunnerDto>(
      `/api/projects/${projectId}/runners/${runnerId}`
    );
    return response.data;
  }

  /**
   * Remove a runner from the project
   */
  async removeRunner(projectId: string, runnerId: string): Promise<void> {
    await apiClient.delete(`/api/projects/${projectId}/runners/${runnerId}`);
  }

  /**
   * List all apps assigned to the project
   */
  async listApps(projectId: string): Promise<ProjectAppDto[]> {
    const response = await apiClient.get<ProjectAppDto[]>(
      `/api/projects/${projectId}/apps`
    );
    return response.data;
  }

  /**
   * Add an app to the project
   */
  async addApp(projectId: string, appId: string): Promise<ProjectAppDto> {
    const response = await apiClient.post<ProjectAppDto>(
      `/api/projects/${projectId}/apps/${appId}`
    );
    return response.data;
  }

  /**
   * Remove an app from the project
   */
  async removeApp(projectId: string, appId: string): Promise<void> {
    await apiClient.delete(`/api/projects/${projectId}/apps/${appId}`);
  }
}

export const projectsService = new ProjectsService();
