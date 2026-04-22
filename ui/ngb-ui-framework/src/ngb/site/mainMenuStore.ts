import { defineStore } from 'pinia'
import { httpGet } from '../api/http'
import { toErrorMessage } from '../utils/errorMessage'

export type MainMenuItem = {
  kind: string
  code: string
  label: string
  route: string
  icon?: string
  ordinal: number
}

export type MainMenuGroup = {
  label: string
  items: MainMenuItem[]
  ordinal: number
  icon?: string | null
}

export type MainMenuDto = {
  groups: MainMenuGroup[]
}

export const useMainMenuStore = defineStore('mainMenu', {
  state: () => ({
    groups: [] as MainMenuGroup[],
    isLoading: false,
    error: null as string | null,
  }),
  actions: {
    async load() {
      this.isLoading = true
      this.error = null

      try {
        const dto = await httpGet<MainMenuDto>('/api/main-menu')
        this.groups = dto?.groups ?? []
      } catch (cause) {
        this.groups = []
        this.error = toErrorMessage(cause, 'Failed to load main menu')
        // eslint-disable-next-line no-console
        console.error(cause)
      } finally {
        this.isLoading = false
      }
    },
  },
})
