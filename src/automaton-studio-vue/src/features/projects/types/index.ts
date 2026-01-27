/**
 * Project feature type definitions
 * Matches backend DTOs from ProjectsController
 */

// Role enum as const object (not enum, per Vue best practices)
export const ProjectRole = {
  ProjectAdmin: 'ProjectAdmin',
  Developer: 'Developer',
  AppUser: 'AppUser',
  Viewer: 'Viewer',
} as const;

export type ProjectRoleType = typeof ProjectRole[keyof typeof ProjectRole];

/**
 * Complete project details
 */
export interface ProjectDto {
  id: string;
  name: string;
  displayName: string;
  description?: string;
  ownerId: string;
  ownerName: string;
  organizationId?: string;
  organizationName?: string;
  createdAt: string;
  updatedAt: string;
  isActive: boolean;
  memberCount: number;
  appCount: number;
  flowCount: number;
  currentUserRole?: ProjectRoleType;
}

/**
 * Lightweight project item for lists and switcher
 */
export interface ProjectListItem {
  id: string;
  name: string;
  displayName: string;
  description?: string;
  role: ProjectRoleType;
  memberCount: number;
  updatedAt: string;
  organizationId?: string;
  organizationName?: string;
}

/**
 * Project member details
 */
export interface ProjectMemberDto {
  projectId: string;
  userId: string;
  userName: string;
  email: string;
  role: ProjectRoleType;
  joinedAt: string;
  invitedBy?: string;
  invitedByName?: string;
  isActive: boolean;
  isOwner: boolean;
}

/**
 * Runner assigned to project
 */
export interface ProjectRunnerDto {
  projectId: string;
  runnerId: string;
  runnerName: string;
  addedAt: string;
  addedBy?: string;
  addedByName: string;
}

/**
 * App assigned to project
 */
export interface ProjectAppDto {
  projectId: string;
  appId: string;
  appName: string;
  addedAt: string;
  addedBy?: string;
  addedByName: string;
}

/**
 * Request to create a new project
 */
export interface CreateProjectRequest {
  name: string;
  displayName: string;
  description?: string;
  organizationId?: string;
}

/**
 * Request to update project details
 */
export interface UpdateProjectRequest {
  displayName: string;
  description?: string;
  isActive?: boolean;
}

/**
 * Request to invite/add a member to project
 */
export interface AddProjectMemberRequest {
  email: string;
  role: ProjectRoleType;
  createAccountIfNotExists?: boolean;
}

/**
 * Request to update a member's role
 */
export interface UpdateProjectMemberRequest {
  role: ProjectRoleType;
}
