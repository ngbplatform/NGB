import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { toErrorMessage } from '../utils/errorMessage'
import { getFeatures } from './api'
import type { FeatureState } from './types'

export const useFeatureStore = defineStore('features', () => {
  const current = ref<FeatureState[]>([])
  const loading = ref(false)
  const error = ref('')
  const loadedAt = ref(0)
  const enabled = computed(() => new Set(current.value.filter(feature => feature.enabled).map(feature => feature.code)))
  let pending: Promise<void> | null = null
  let generation = 0

  function isEnabled(code: string): boolean {
    return enabled.value.has(code)
  }

  function load(force = false): Promise<void> {
    if (pending) return pending
    if (!force && loadedAt.value && Date.now() - loadedAt.value < 30000) return Promise.resolve()

    const requestGeneration = generation
    loading.value = true
    error.value = ''
    pending = (async () => {
      try {
        const result = await getFeatures()
        if (requestGeneration !== generation) return
        current.value = result
        loadedAt.value = Date.now()
      } catch (cause) {
        if (requestGeneration !== generation) return
        current.value = []
        loadedAt.value = 0
        error.value = toErrorMessage(cause, 'Could not load available features')
      } finally {
        if (requestGeneration === generation) {
          loading.value = false
          pending = null
        }
      }
    })()

    return pending
  }

  function reset(): void {
    generation++
    current.value = []
    loadedAt.value = 0
    loading.value = false
    error.value = ''
    pending = null
  }

  return { current, loading, error, isEnabled, load, reset }
})
