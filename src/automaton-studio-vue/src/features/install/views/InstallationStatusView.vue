<script setup lang="ts">
import Card from 'primevue/card'
import Message from 'primevue/message'
import SetupChecks from '../components/SetupChecks.vue'
</script>
<template>
  <div class="flex flex-column gap-4">
    <div><h1 class="text-2xl font-bold mb-2">Installation</h1><p class="text-color-secondary">Check service connections and configuration without revealing saved secrets.</p></div>
    <Card><template #title>Configuration status</template><template #content><SetupChecks /></template></Card>
    <Message severity="info" :closable="false">Keep encrypted backups of your server configuration, database and persistent keys. Presence checks do not verify backup or restore coverage.</Message>
    <Card><template #title>Next steps</template><template #content>
      <p>Install a Machine Management Runner from Runners to deploy applications. The control plane does not execute your applications itself.</p>
      <p>Optional SMTP, OAuth and billing settings are managed in the server configuration. Save changes on the host and recreate API and Worker to apply them.</p>
    </template></Card>
  </div>
</template>
