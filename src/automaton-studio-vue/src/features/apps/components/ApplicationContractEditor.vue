<script setup lang="ts">
import { reactive, watch } from 'vue'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import InputNumber from 'primevue/inputnumber'
import InputSwitch from 'primevue/inputswitch'
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

watch(
  () => props.modelValue,
  (value) => Object.assign(draft, clone(value)),
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

function setCommandEnabled(section: 'build' | 'runtime', enabled: boolean) {
  draft[section].command = enabled ? commandFor(section) : null
  updateContract()
}

function updateContract() {
  emit('update:modelValue', clone(draft))
}

</script>

<template>
  <div class="contract-editor flex flex-column gap-5">
    <div>
      <h3 class="mt-0 mb-2">Build</h3>
      <p class="text-color-secondary mt-0">
        The assigned Runner executes this structured command once during deployment. Leave it disabled for an in-place or static artifact.
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
          <Textarea id="build-arguments" :model-value="formatLines(draft.build.command.arguments)" rows="3" @update:model-value="updateCommand('build', { arguments: parseLines($event) })" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="build-environment">Environment <small>(KEY=value per line)</small></label>
          <Textarea id="build-environment" :model-value="formatEnvironment(draft.build.command.environment)" rows="3" @update:model-value="updateCommand('build', { environment: parseEnvironment($event) })" />
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
          <Textarea id="runtime-arguments" :model-value="formatLines(draft.runtime.command.arguments)" rows="3" @update:model-value="updateCommand('runtime', { arguments: parseLines($event) })" />
        </div>
        <div class="col-12 md:col-6 flex flex-column gap-2">
          <label for="runtime-environment">Environment <small>(KEY=value per line)</small></label>
          <Textarea id="runtime-environment" :model-value="formatEnvironment(draft.runtime.command.environment)" rows="3" @update:model-value="updateCommand('runtime', { environment: parseEnvironment($event) })" />
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
    </div>
  </div>
</template>
