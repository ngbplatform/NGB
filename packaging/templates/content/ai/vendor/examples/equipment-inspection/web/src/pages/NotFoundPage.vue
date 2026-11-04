<script setup lang="ts">
import { useRouter } from 'vue-router'
import { NgbPageHeader } from '@ngbplatform/ui/layout'

const router = useRouter()
</script>

<template>
  <NgbPageHeader title="Page not found" :can-back="true" @back="router.push('/catalogs/example.equipment')" />
  <p class="p-6 text-ngb-muted">This address does not match an application page. Return to the equipment list to continue.</p>
</template>
