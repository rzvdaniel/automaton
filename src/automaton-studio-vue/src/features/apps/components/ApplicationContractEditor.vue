<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import InputNumber from 'primevue/inputnumber'
import InputSwitch from 'primevue/inputswitch'
import Button from 'primevue/button'
import type {
  ApplicationCommandSpec,
  ApplicationContract,
} from '../types/apps.types'

const props = defineProps<{ modelValue: ApplicationContract }>()
const emit = defineEmits<{ 'update:modelValue': [value: ApplicationContract] }>()

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

const draft = reactive<ApplicationContract>(clone(props.modelValue))
const buildArgumentsText = ref('')
const buildEnvironmentText = ref('')
const runtimeArgumentsText = ref('')
const runtimeEnvironmentText = ref('')

function syncTextBuffers(): void {
  buildArgumentsText.value = formatLines(draft.build.command?.arguments ?? [])
  buildEnvironmentText.value = formatEnvironment(draft.build.command?.environment)
  runtimeArgumentsText.value = formatLines(draft.runtime.command?.arguments ?? [])
  runtimeEnvironmentText.value = formatEnvironment(draft.runtime.command?.environment)
}

watch(
  () => props.modelValue,
  (value) => {
    Object.assign(draft, clone(value))
    syncTextBuffers()
  },
  { deep: true },
)

function parseLines(value: string): string[] {
  return value.split('\n').map((line) => line.trim()).filter(Boolean)
}

function formatLines(value: string[]): string {
  return value.join('\n')
}

function parseEnvironment(value: string): Record<string, string> {
  return Object.fromEntries(
    parseLines(value)
      .map((line) => {
        const separator = line.indexOf('=')
        return separator > 0 ? [line.slice(0, separator).trim(), line.slice(separator + 1)] : null
      })
      .filter((entry): entry is [string, string] => entry !== null),
  )
}

function formatEnvironment(value: Record<string, string> | null | undefined): string {
  return Object.entries(value ?? {}).map(([key, entry]) => `${key}=${entry}`).join('\n')
}

function commandFor(section: 'build' | 'runtime'): ApplicationCommandSpec {
  const command = draft[section].command
  return command ?? {
    executable: '',
    arguments: [],
    workingDirectory: '.',
    environment: {},
    useShell: false,
  }
}

function updateCommand(section: 'build' | 'runtime', update: Partial<ApplicationCommandSpec>) {
  draft[section].command = { ...commandFor(section), ...update }
  emit('update:modelValue', clone(draft))
}

function setCommandText(section: 'build' | 'runtime', field: 'arguments' | 'environment', value: string): void {
  if (section === 'build' && field === 'arguments') buildArgumentsText.value = value
  if (section === 'build' && field === 'environment') buildEnvironmentText.value = value
  if (section === 'runtime' && field === 'arguments') runtimeArgumentsText.value = value
  if (section === 'runtime' && field === 'environment') runtimeEnvironmentText.value = value
}

function commitCommandText(section: 'build' | 'runtime', field: 'arguments' | 'environment'): void {
  const value = section === 'build'
    ? field === 'arguments' ? buildArgumentsText.value : buildEnvironmentText.value
    : field === 'arguments' ? runtimeArgumentsText.value : runtimeEnvironmentText.value
  updateCommand(section, { [field]: field === 'arguments' ? parseLines(value) : parseEnvironment(value) })
}

function setCommandEnabled(section: 'build' | 'runtime', enabled: boolean) {
  draft[section].command = enabled ? commandFor(section) : null
  syncTextBuffers()
  updateContract()
}

function updateMount(index: number, field: 'name' | 'relativePath', value: string): void {
  draft.storage.mounts = draft.storage.mounts.map((mount, mountIndex) =>
    mountIndex === index ? { ...mount, [field]: value } : mount,
  )
  updateContract()
}

function addMount(): void {
  draft.storage.mounts.push({ name: '', relativePath: '', readOnly: false })
  updateContract()
}

function removeMount(index: number): void {
  draft.storage.mounts.splice(index, 1)
  updateContract()
}

syncTextBuffers()

function updateContract() {
  emit('update:modelValue', clone(draft))
}

</script>

<template>
  <div class="contract-editor flex flex-column gap-5">
    <div>
      <h3 class="mt-0 mb-2">Build</h3>
      <p class="text-color-secondary mt-0">
        The assigned Runner executes this structured command once during deployment. Leave it disabled for finite-work applications without an HTTP endpoint.
      </p>
      <div class="flex align-items-center gap-2 mb-3">
        <InputSwitch :model-value="draft.build.command !== null" input-id="build-enabled" @update:model-value="setCommandEnabled('build', $event)" />
        <label for="build-enabled">Build before starting the runtime</label>
      </div>
      <div v-if="draft.build.command" class="grid">
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="build-executable">Executable</label>
          <InputText id="build-executable" :model-value="draft.build.command.executable" placeholder="build-tool" @update:model-value="updateCommand('build', { executable: $event })" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="build-working-directory">Working directory</label>
          <InputText id="build-working-directory" :model-value="draft.build.command.workingDirectory" placeholder="." @update:model-value="updateCommand('build', { workingDirectory: $event })" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="build-arguments">Arguments <small>(one per line)</small></label>
          <Textarea id="build-arguments" :model-value="buildArgumentsText" rows="3" @update:model-value="setCommandText('build', 'arguments', $event || '')" @blur="commitCommandText('build', 'arguments')" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="build-environment">Environment <small>(KEY=value per line)</small></label>
          <Textarea id="build-environment" :model-value="buildEnvironmentText" rows="3" @update:model-value="setCommandText('build', 'environment', $event || '')" @blur="commitCommandText('build', 'environment')" />
        </div>
        <div class="col-12 md:col-6 flex align-items-center gap-2">
          <InputSwitch :model-value="draft.build.command.useShell" input-id="build-use-shell" @update:model-value="updateCommand('build', { useShell: $event })" />
          <label for="build-use-shell">Use shell (advanced explicit semantics)</label>
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="build-output-directory">Output directory <small>(optional)</small></label>
          <InputText id="build-output-directory" v-model="draft.build.outputDirectory" placeholder="dist" @update:model-value="updateContract" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="build-timeout">Timeout (seconds)</label>
          <InputNumber id="build-timeout" v-model="draft.build.timeoutSeconds" :min="1" :max="86400" @update:model-value="updateContract" />
        </div>
      </div>
    </div>

    <div>
      <h3 class="mt-0 mb-2">Runtime</h3>
      <p class="text-color-secondary mt-0">Describe the process that listens for application traffic. Automaton does not infer a language or framework.</p>
      <div class="flex align-items-center gap-2 mb-3">
        <InputSwitch :model-value="draft.runtime.command !== null" input-id="runtime-enabled" @update:model-value="setCommandEnabled('runtime', $event)" />
        <label for="runtime-enabled">Start a runtime process</label>
      </div>
      <div v-if="draft.runtime.command" class="grid">
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="runtime-executable">Executable</label>
          <InputText id="runtime-executable" :model-value="draft.runtime.command.executable" placeholder="app-server" @update:model-value="updateCommand('runtime', { executable: $event })" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="runtime-working-directory">Working directory</label>
          <InputText id="runtime-working-directory" :model-value="draft.runtime.command.workingDirectory" placeholder="." @update:model-value="updateCommand('runtime', { workingDirectory: $event })" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="runtime-arguments">Arguments <small>(one per line)</small></label>
          <Textarea id="runtime-arguments" :model-value="runtimeArgumentsText" rows="3" @update:model-value="setCommandText('runtime', 'arguments', $event || '')" @blur="commitCommandText('runtime', 'arguments')" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="runtime-environment">Environment <small>(KEY=value per line)</small></label>
          <Textarea id="runtime-environment" :model-value="runtimeEnvironmentText" rows="3" @update:model-value="setCommandText('runtime', 'environment', $event || '')" @blur="commitCommandText('runtime', 'environment')" />
        </div>
        <div class="col-12 md:col-6 flex align-items-center gap-2">
          <InputSwitch :model-value="draft.runtime.command.useShell" input-id="runtime-use-shell" @update:model-value="updateCommand('runtime', { useShell: $event })" />
          <label for="runtime-use-shell">Use shell (advanced explicit semantics)</label>
        </div>
      </div>
      <div class="grid">
        <div class="col-12 md:col-4 flex flex-column gap-2">
          <label for="runtime-port">Port</label>
          <InputNumber id="runtime-port" v-model="draft.runtime.port" :min="1" :max="65535" @update:model-value="updateContract" />
        </div>
      </div>
    </div>

    <div>
      <h3 class="mt-0 mb-2">Readiness and storage</h3>
      <div class="grid">
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="health-path">Health path <small>(optional)</small></label>
          <InputText id="health-path" v-model="draft.health.path" placeholder="/health" @update:model-value="updateContract" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="health-port">Health port <small>(optional)</small></label>
          <InputNumber id="health-port" v-model="draft.health.port" :min="1" :max="65535" @update:model-value="updateContract" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="startup-timeout">Startup timeout (seconds)</label>
          <InputNumber id="startup-timeout" v-model="draft.health.startupTimeoutSeconds" :min="1" :max="86400" @update:model-value="updateContract" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="data-directory">Persistent data directory</label>
          <InputText id="data-directory" v-model="draft.storage.dataDirectory" placeholder="data" @update:model-value="updateContract" />
        </div>
      </div>
        <small class="text-color-secondary">Persistent data is kept with the application and is not part of disposable runtime artifacts.</small>
      <div class="mt-3 flex flex-column gap-2">
        <div class="flex align-items-center justify-content-between">
          <label>Storage mounts</label>
          <Button label="Add mount" icon="pi pi-plus" text size="small" @click="addMount" />
        </div>
        <small class="text-color-secondary">Mount names and workspace-relative paths are persisted. Read-only mounts are not supported.</small>
        <div v-for="(mount, index) in draft.storage.mounts" :key="index" class="grid align-items-end">
          <div class="col-12 md:col-5 flex flex-column gap-2">
            <label :for="`storage-mount-name-${index}`">Name</label>
            <InputText :id="`storage-mount-name-${index}`" :model-value="mount.name" placeholder="uploads" @update:model-value="updateMount(index, 'name', $event || '')" />
          </div>
          <div class="col-12 md:col-5 flex flex-column gap-2">
            <label :for="`storage-mount-path-${index}`">Relative path</label>
            <InputText :id="`storage-mount-path-${index}`" :model-value="mount.relativePath" placeholder="data/uploads" @update:model-value="updateMount(index, 'relativePath', $event || '')" />
          </div>
          <div class="col-12 md:col-2">
            <Button label="Remove" icon="pi pi-trash" severity="secondary" text size="small" @click="removeMount(index)" />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
