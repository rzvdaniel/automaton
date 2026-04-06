/**
 * Organization feature type definitions
 * Matches backend DTOs from OrganizationsController
 */

// OrganizationRole enum as const object (not enum, per Vue best practices)
export const OrganizationRole = {
  OrgAdmin: 'OrgAdmin',
  Developer: 'Developer',
  AppUser: 'AppUser',
} as const;

export type OrganizationRoleType = typeof OrganizationRole[keyof typeof OrganizationRole];

/**
 * Complete organization details
 */
export interface OrganizationDto {
  id: string;
  name: string;
  description?: string;
  ownerId: string;
  ownerName: string;
  slug?: string;
  allowPublicSignup: boolean;
  requireEmailDomain?: string;
  logoUrl?: string;
  primaryColor?: string;
  createdAt: string;
  updatedAt?: string;
  isActive: boolean;
  memberCount: number;
  projectCount: number;
  currentUserRole?: OrganizationRoleType;
  appVerificationDefaultStrategy?: string;
  appVerificationAllowUserOverride: boolean;
}

/**
 * Lightweight organization item for lists
 */
export interface OrganizationListItem {
  id: string;
  name: string;
  description?: string;
  slug?: string;
  logoUrl?: string;
  role: OrganizationRoleType;
  memberCount: number;
  projectCount: number;
  updatedAt: string;
}

/**
 * Organization member details
 */
export interface OrganizationMemberDto {
  id: string;
  organizationId: string;
  userId: string;
  userName: string;
  email: string;
  role: OrganizationRoleType;
  joinedAt: string;
  invitedBy?: string;
  invitedByName?: string;
  isActive: boolean;
  isOwner: boolean;
}

/**
 * Organization app assignment details
 */
export interface OrganizationAppAssignmentDto {
  id: string;
  organizationId: string;
  appId: string;
  appName: string;
  userId: string;
  userName: string;
  userEmail: string;
  assignedBy?: string;
  assignedByName?: string;
  assignedAt: string;
}

/**
 * Request to create a new organization
 */
export interface CreateOrganizationRequest {
  name: string;
  description?: string;
  slug?: string;
  allowPublicSignup?: boolean;
  requireEmailDomain?: string;
}

/**
 * Request to update organization details
 */
export interface UpdateOrganizationRequest {
  name: string;
  description?: string;
  allowPublicSignup: boolean;
  requireEmailDomain?: string;
  logoUrl?: string;
  primaryColor?: string;
  appVerificationDefaultStrategy?: string;
  appVerificationAllowUserOverride: boolean;
}

/**
 * Request to invite a member to organization
 */
export interface InviteMemberRequest {
  email: string;
  role: OrganizationRoleType;
}

/**
 * Request to accept an organization invitation
 */
export interface AcceptInvitationRequest {
  token: string;
}

/**
 * Request to update a member's role
 */
export interface UpdateOrganizationMemberRequest {
  role: OrganizationRoleType;
}

/**
 * Request to assign an app to a user
 */
export interface AssignAppRequest {
  userId: string;
}
/**
 * Pending organization invitation
 */
export interface PendingInvitationDto {
  invitationId: string;
  organizationId: string;
  organizationName: string;
  organizationSlug: string;
  role: OrganizationRoleType;
  invitedAt: string;
  expiresAt: string;
  token: string;
}