import { ApiError, httpGet } from '../api/http'
import type { FeatureState } from './types'

export async function getFeatures(): Promise<FeatureState[]> {
  try {
    return await httpGet<FeatureState[]>('/api/features')
  } catch (cause) {
    // A 3.0 host has no feature discovery endpoint. New UI capabilities stay disabled.
    if (cause instanceof ApiError && cause.status === 404) return []
    throw cause
  }
}
