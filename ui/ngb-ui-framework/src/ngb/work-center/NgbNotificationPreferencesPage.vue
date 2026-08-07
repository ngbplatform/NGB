<template>
  <div class="min-h-0 flex-1 overflow-auto bg-ngb-bg p-4 md:p-6">
    <div class="mx-auto max-w-[820px]">
      <h1 class="text-2xl font-semibold text-ngb-text">Work Center preferences</h1>
      <p class="mt-1 text-sm text-ngb-muted">
        Choose which tasks and informational notifications appear in your Work Center.
      </p>

      <div v-if="loading" class="mt-6 text-sm text-ngb-muted">Loading…</div>
      <div v-else-if="error" class="mt-6 rounded-[var(--ngb-radius)] border border-ngb-danger/30 bg-ngb-danger/5 p-4 text-sm text-ngb-danger">{{ error }}</div>
      <div v-else class="mt-6 space-y-5">
        <section v-for="[category, items] in grouped" :key="category" class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card">
          <div class="border-b border-ngb-border px-4 py-3 text-sm font-semibold text-ngb-text">{{ category }}</div>
          <label v-for="item in items" :key="`${item.code}:${item.channel}`" class="flex items-start gap-3 border-b border-ngb-border px-4 py-4 last:border-b-0">
            <input v-model="item.isEnabled" type="checkbox" class="mt-1 rounded border-ngb-border"
              :disabled="item.isMandatory || !item.userCanDisable">
            <span class="min-w-0">
              <span class="block text-sm font-semibold text-ngb-text">{{ item.displayName }}</span>
              <span class="mt-1 block text-xs text-ngb-muted">
                {{ item.description ?? (item.isMandatory ? `Required ${item.kind.toLowerCase()}` : 'Enabled in Work Center') }}
              </span>
            </span>
          </label>
        </section>
        <div class="flex justify-end">
          <button type="button" class="rounded-[var(--ngb-radius)] bg-ngb-blue px-4 py-2 text-sm font-semibold text-white ngb-focus"
            :disabled="saving" @click="save">{{ saving ? 'Saving…' : 'Save preferences' }}</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getNotificationPreferences, updateNotificationPreferences } from './api'
import type { NotificationPreference } from './types'

const preferences = ref<NotificationPreference[]>([])
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)
const grouped = computed(() => {
  const groups = new Map<string, NotificationPreference[]>()
  const ordered = [...preferences.value].sort((left, right) => {
    if (left.kind !== right.kind) return left.kind === 'Task' ? -1 : 1
    return left.category.localeCompare(right.category) || left.displayName.localeCompare(right.displayName)
  })
  for (const preference of ordered) {
    const items = groups.get(preference.category) ?? []
    items.push(preference)
    groups.set(preference.category, items)
  }
  return Array.from(groups.entries())
})

function message(cause: unknown) {
  return cause instanceof Error ? cause.message : 'Unable to update Work Center preferences.'
}

async function load() {
  loading.value = true
  error.value = null
  try {
    preferences.value = await getNotificationPreferences()
  } catch (cause) {
    error.value = message(cause)
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  error.value = null
  try {
    await updateNotificationPreferences(preferences.value.map(({ code, channel, isEnabled }) => ({
      code,
      channel,
      isEnabled,
    })))
    await load()
  } catch (cause) {
    error.value = message(cause)
  } finally {
    saving.value = false
  }
}

onMounted(() => { void load() })
</script>
