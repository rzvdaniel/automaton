<template>
  <div class="project-settings">
    <!-- Header -->
    <div class="flex items-center justify-between mb-4">
      <div class="flex items-center gap-3">
        <Button
          icon="pi pi-arrow-left"
          text
          rounded
          aria-label="Back to projects"
          @click="$router.push('/projects')"
        />
        <h1 class="text-3xl font-bold">{{ projectTitle }}</h1>
      </div>
    </div>

    <!-- Error State -->
    <Message v-if="error" severity="error" :closable="false">
      {{ error }}
    </Message>

    <!-- Loading State -->
    <div v-if="initialLoading" class="flex justify-center items-center" style="min-height: 400px">
      <ProgressSpinner />
    </div>

    <!-- Content -->
    <div v-else-if="project">
      <TabView v-model:active-index="activeTab">
        <!-- Members Tab -->
        <TabPanel value="0" header="Members">
          <MembersTab :project-id="projectId" :current-user-role="currentUserRole" />
        </TabPanel>

        <!-- Runners Tab -->
        <TabPanel value="1" header="Runners">
          <RunnersTab :project-id="projectId" :current-user-role="currentUserRole" />
        </TabPanel>

        <!-- Apps Tab -->
        <TabPanel value="2" header="Apps">
          <AppsTab :project-id="projectId" :current-user-role="currentUserRole" />
        </TabPanel>
      </TabView>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useProjectsStore } from '../stores/projectsStore';
import { useAuthStore } from '@/features/auth/stores/auth.store';
import MembersTab from '../components/MembersTab.vue';
import RunnersTab from '../components/RunnersTab.vue';
import AppsTab from '../components/AppsTab.vue';
import Button from 'primevue/button';
import TabView from 'primevue/tabview';
import TabPanel from 'primevue/tabpanel';
import Message from 'primevue/message';
import ProgressSpinner from 'primevue/progressspinner';

const route = useRoute();
const router = useRouter();
const store = useProjectsStore();
const authStore = useAuthStore();

const projectId = computed(() => route.params.id as string);
const activeTab = ref(0);
const error = ref<string>('');
const initialLoading = ref(true);

const project = computed(() => {
  return store.projects.find((p) => p.id === projectId.value);
});

const projectTitle = computed(() => {
  return project.value?.name || 'Project Settings';
});

const currentUserRole = computed(() => {
  // TODO: Get from members list when we load project details
  // For now, assume user is ProjectAdmin
  const member = store.members.find((m) => m.userId === authStore.user?.id);
  return member?.role || 'ProjectAdmin';
});

async function loadData() {
  initialLoading.value = true;
  error.value = '';

  // Check authentication
  if (!authStore.isAuthenticated) {
    router.push('/auth/login');
    return;
  }

  try {
    // Load projects if not already loaded
    if (store.projects.length === 0) {
      await store.loadProjects();
    }

    // Verify project exists
    if (!project.value) {
      error.value = 'Project not found';
      initialLoading.value = false;
      return;
    }

    // Load project details (members, runners, apps)
    await store.loadProjectDetails(projectId.value);
  } catch (err: any) {
    if (err?.response?.status === 401) {
      router.push('/auth/login');
    } else {
      error.value = err?.message || 'Failed to load project details';
    }
  } finally {
    initialLoading.value = false;
  }
}

onMounted(() => {
  loadData();
});
</script>

<style scoped>
.project-settings {
  padding: 1.5rem;
}
</style>
