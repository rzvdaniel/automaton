<template>
  <div class="apps-tab">
    <!-- Add App Form -->
    <Card v-if="canManageResources" class="mb-4">
      <template #title>
        <div class="flex items-center gap-2">
          <i class="pi pi-mobile"></i>
          <span>Add App</span>
        </div>
      </template>
      <template #content>
        <div class="flex gap-3">
          <div class="flex-1">
            <label for="app-select" class="block text-sm font-medium mb-2">Select App</label>
            <Select
              id="app-select"
              v-model="selectedApp"
              :options="availableApps"
              option-label="name"
              option-value="id"
              placeholder="Choose an app"
              class="w-full"
              filter
            >
              <template #option="{ option }">
                <div class="flex items-center gap-2">
                  <i class="pi pi-mobile"></i>
                  <span>{{ option.name }}</span>
                  <Tag v-if="option.isPublished" severity="success" value="Published" />
                  <Tag v-else severity="secondary" value="Draft" />
                </div>
              </template>
            </Select>
          </div>
          <div class="flex items-end">
            <Button
              label="Add App"
              icon="pi pi-plus"
              @click="handleAddApp"
              :disabled="!selectedApp"
            />
          </div>
        </div>
      </template>
    </Card>

    <!-- Apps DataTable -->
    <DataTable
      :value="apps"
      :loading="loading"
      paginator
      :rows="10"
      dataKey="appId"
      emptyMessage="No apps assigned to this project"
    >
      <Column field="appName" header="App Name" sortable>
        <template #body="{ data }">
          <div class="flex items-center gap-2">
            <i class="pi pi-mobile"></i>
            <span>{{ data.appName }}</span>
          </div>
        </template>
      </Column>
      <Column field="addedAt" header="Added" sortable>
        <template #body="{ data }">
          {{ formatDate(data.addedAt) }}
        </template>
      </Column>
      <Column field="addedByName" header="Added By" sortable />
      <Column header="Actions" style="width: 80px">
        <template #body="{ data }">
          <Button
            v-if="canManageResources"
            icon="pi pi-trash"
            severity="danger"
            text
            rounded
            @click="handleRemoveApp(data)"
            aria-label="Remove app"
          />
        </template>
      </Column>
    </DataTable>

    <ConfirmDialog />
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { useToast } from 'primevue/usetoast';
import { useConfirm } from 'primevue/useconfirm';
import { useProjectsStore } from '../stores/projectsStore';
import { ProjectRole, type ProjectAppDto } from '../types';
import Card from 'primevue/card';
import DataTable from 'primevue/datatable';
import Column from 'primevue/column';
import Select from 'primevue/select';
import Tag from 'primevue/tag';
import Button from 'primevue/button';
import ConfirmDialog from 'primevue/confirmdialog';

interface Props {
  projectId: string;
  currentUserRole?: string;
}

const props = defineProps<Props>();

const toast = useToast();
const confirm = useConfirm();
const store = useProjectsStore();

const selectedApp = ref<string>('');

// TODO: Replace with actual API call to fetch available apps
const availableApps = ref([
  { id: 'app-1', name: 'Customer Portal', isPublished: true },
  { id: 'app-2', name: 'Admin Dashboard', isPublished: true },
  { id: 'app-3', name: 'Analytics App', isPublished: false },
]);

const apps = computed(() => store.apps);
const loading = computed(() => store.loading);

const canManageResources = computed(() => {
  return (
    props.currentUserRole === ProjectRole.ProjectAdmin ||
    props.currentUserRole === ProjectRole.Developer
  );
});

function formatDate(dateString: string): string {
  if (!dateString) return 'N/A';
  const date = new Date(dateString);
  return date.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

async function handleAddApp() {
  if (!selectedApp.value) {
    toast.add({
      severity: 'error',
      summary: 'Validation Error',
      detail: 'Please select an app',
      life: 3000,
    });
    return;
  }

  try {
    await store.addApp(props.projectId, selectedApp.value);
    selectedApp.value = '';
  } catch (error) {
    // Error handled by service
  }
}

function handleRemoveApp(app: ProjectAppDto) {
  confirm.require({
    message: `Are you sure you want to remove app "${app.appName}" from this project? This may affect flows or other dependencies.`,
    header: 'Remove App',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await store.removeApp(props.projectId, app.appId);
      } catch (error) {
        // Error handled by service
      }
    },
  });
}
</script>
