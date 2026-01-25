<template>
  <div class="runners-tab">
    <!-- Add Runner Form -->
    <Card v-if="canManageResources" class="mb-4">
      <template #title>
        <div class="flex items-center gap-2">
          <i class="pi pi-server"></i>
          <span>Add Runner</span>
        </div>
      </template>
      <template #content>
        <div class="flex gap-3">
          <div class="flex-1">
            <label for="runner-select" class="block text-sm font-medium mb-2">
              Select Runner
            </label>
            <Dropdown
              id="runner-select"
              v-model="selectedRunner"
              :options="availableRunners"
              option-label="name"
              option-value="id"
              placeholder="Choose a runner"
              class="w-full"
              filter
            >
              <template #option="{ option }">
                <div class="flex items-center gap-2">
                  <i
                    class="pi"
                    :class="option.isOnline ? 'pi-circle-fill text-green-500' : 'pi-circle text-gray-400'"
                  ></i>
                  <span>{{ option.name }}</span>
                  <Tag v-if="!option.isOnline" severity="secondary" value="Offline" />
                </div>
              </template>
            </Dropdown>
          </div>
          <div class="flex items-end">
            <Button
              label="Add Runner"
              icon="pi pi-plus"
              @click="handleAddRunner"
              :disabled="!selectedRunner"
            />
          </div>
        </div>
      </template>
    </Card>

    <!-- Runners DataTable -->
    <DataTable
      :value="runners"
      :loading="loading"
      paginator
      :rows="10"
      dataKey="runnerId"
      emptyMessage="No runners assigned to this project"
    >
      <Column field="runnerName" header="Runner Name" sortable>
        <template #body="{ data }">
          <div class="flex items-center gap-2">
            <i class="pi pi-server"></i>
            <span>{{ data.runnerName }}</span>
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
            @click="handleRemoveRunner(data)"
            aria-label="Remove runner"
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
import { ProjectRole, type ProjectRunnerDto } from '../types';
import Card from 'primevue/card';
import DataTable from 'primevue/datatable';
import Column from 'primevue/column';
import Dropdown from 'primevue/dropdown';
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

const selectedRunner = ref<string>('');

// TODO: Replace with actual API call to fetch available runners
const availableRunners = ref([
  { id: 'runner-1', name: 'Production Runner', isOnline: true },
  { id: 'runner-2', name: 'Development Runner', isOnline: true },
  { id: 'runner-3', name: 'Test Runner', isOnline: false },
]);

const runners = computed(() => store.runners);
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

async function handleAddRunner() {
  if (!selectedRunner.value) {
    toast.add({
      severity: 'error',
      summary: 'Validation Error',
      detail: 'Please select a runner',
      life: 3000,
    });
    return;
  }

  try {
    await store.addRunner(props.projectId, selectedRunner.value);
    selectedRunner.value = '';
  } catch (error) {
    // Error handled by service
  }
}

function handleRemoveRunner(runner: ProjectRunnerDto) {
  confirm.require({
    message: `Are you sure you want to remove runner "${runner.runnerName}" from this project? Flows will no longer be able to use this runner.`,
    header: 'Remove Runner',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await store.removeRunner(props.projectId, runner.runnerId);
      } catch (error) {
        // Error handled by service
      }
    },
  });
}
</script>
