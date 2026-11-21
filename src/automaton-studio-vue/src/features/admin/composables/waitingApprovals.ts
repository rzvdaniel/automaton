import { ref } from 'vue'

/** Bumped when a permission request is approved or denied, so the sidebar badge refetches its count. */
export const waitingChanged = ref(0)
export const notifyWaitingChanged = () => { waitingChanged.value++ }
