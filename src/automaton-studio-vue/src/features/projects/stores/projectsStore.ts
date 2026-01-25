/**
 * Projects Pinia Store
 * Manages global state for projects, active project, and project details
 */

import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { projectsService } from '../services/projectsService';
import type {
  ProjectListItem,
  ProjectMemberDto,
  ProjectRunnerDto,
  ProjectAppDto,
  CreateProjectRequest,
  AddProjectMemberRequest,
  UpdateProjectMemberRequest,
} from '../types';

const ACTIVE_PROJECT_STORAGE_KEY = 'automaton:activeProjectId';

export const useProjectsStore = defineStore('projects', () => {
  // State
  const projects = ref<ProjectListItem[]>([]);
  const activeProjectId = ref<string | null>(null);
  const members = ref<ProjectMemberDto[]>([]);
  const runners = ref<ProjectRunnerDto[]>([]);
  const apps = ref<ProjectAppDto[]>([]);
  const loading = ref(false);

  // Computed
  const activeProject = computed(() => {
    if (!activeProjectId.value) return null;
    return projects.value.find((p) => p.id === activeProjectId.value) || null;
  });

  // Actions

  /**
   * Load all projects for the current user
   */
  async function loadProjects() {
    loading.value = true;
    try {
      const data = await projectsService.listProjects();
      projects.value = data;
    } finally {
      loading.value = false;
    }
  }

  /**
   * Set the active project and persist to localStorage
   */
  function setActiveProject(projectId: string | null) {
    activeProjectId.value = projectId;
    if (projectId === null) {
      localStorage.removeItem(ACTIVE_PROJECT_STORAGE_KEY);
    } else {
      localStorage.setItem(ACTIVE_PROJECT_STORAGE_KEY, projectId);
    }
  }

  /**
   * Create a new project and add to list
   */
  async function createProject(request: CreateProjectRequest) {
    loading.value = true;
    try {
      const project = await projectsService.createProject(request);
      // Reload projects to get the new one with all details
      await loadProjects();
      return project;
    } finally {
      loading.value = false;
    }
  }

  /**
   * Delete (deactivate) a project
   */
  async function deleteProject(projectId: string) {
    loading.value = true;
    try {
      await projectsService.deleteProject(projectId);
      projects.value = projects.value.filter((p) => p.id !== projectId);
      // Clear active project if it was deleted
      if (activeProjectId.value === projectId) {
        setActiveProject(null);
      }
    } finally {
      loading.value = false;
    }
  }

  /**
   * Load project details including members, runners, and apps
   */
  async function loadProjectDetails(projectId: string) {
    loading.value = true;
    try {
      const [membersList, runnersList, appsList] = await Promise.all([
        projectsService.listMembers(projectId),
        projectsService.listRunners(projectId),
        projectsService.listApps(projectId),
      ]);
      members.value = membersList;
      runners.value = runnersList;
      apps.value = appsList;
    } finally {
      loading.value = false;
    }
  }

  /**
   * Invite a member to the project
   */
  async function addMember(projectId: string, request: AddProjectMemberRequest) {
    try {
      const member = await projectsService.addMember(projectId, request);
      members.value.push(member);
      return member;
    } catch (error) {
      throw error;
    }
  }

  /**
   * Remove a member from the project
   */
  async function removeMember(projectId: string, userId: string) {
    try {
      await projectsService.removeMember(projectId, userId);
      members.value = members.value.filter((m) => m.userId !== userId);
    } catch (error) {
      throw error;
    }
  }

  /**
   * Update a member's role in the project
   */
  async function updateMemberRole(projectId: string, userId: string, request: UpdateProjectMemberRequest) {
    try {
      const updated = await projectsService.updateMemberRole(projectId, userId, request);
      const index = members.value.findIndex((m) => m.userId === userId);
      if (index !== -1) {
        members.value[index] = updated;
      }
      return updated;
    } catch (error) {
      throw error;
    }
  }

  /**
   * Add a runner to the project
   */
  async function addRunner(projectId: string, runnerId: string) {
    try {
      const runner = await projectsService.addRunner(projectId, runnerId);
      runners.value.push(runner);
      return runner;
    } catch (error) {
      throw error;
    }
  }

  /**
   * Remove a runner from the project
   */
  async function removeRunner(projectId: string, runnerId: string) {
    try {
      await projectsService.removeRunner(projectId, runnerId);
      runners.value = runners.value.filter((r) => r.runnerId !== runnerId);
    } catch (error) {
      throw error;
    }
  }

  /**
   * Add an app to the project
   */
  async function addApp(projectId: string, appId: string) {
    try {
      const app = await projectsService.addApp(projectId, appId);
      apps.value.push(app);
      return app;
    } catch (error) {
      throw error;
    }
  }

  /**
   * Remove an app from the project
   */
  async function removeApp(projectId: string, appId: string) {
    try {
      await projectsService.removeApp(projectId, appId);
      apps.value = apps.value.filter((a) => a.appId !== appId);
    } catch (error) {
      throw error;
    }
  }

  /**
   * Initialize store: restore active project from localStorage and load projects
   */
  async function initialize() {
    const storedProjectId = localStorage.getItem(ACTIVE_PROJECT_STORAGE_KEY);
    if (storedProjectId) {
      activeProjectId.value = storedProjectId;
    }
    await loadProjects();
  }

  return {
    // State
    projects,
    activeProjectId,
    members,
    runners,
    apps,
    loading,

    // Computed
    activeProject,

    // Actions
    initialize,
    loadProjects,
    setActiveProject,
    createProject,
    deleteProject,
    loadProjectDetails,
    addMember,
    removeMember,
    updateMemberRole,
    addRunner,
    removeRunner,
    addApp,
    removeApp,
  };
});
