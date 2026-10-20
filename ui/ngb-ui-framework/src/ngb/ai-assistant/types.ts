import type { NgbSourceIdentity } from '../api/sourceKinds'
import type { NgbPageContext } from '../navigation/types'

export type AssistantPageContext = NgbPageContext

export type AssistantSource = NgbSourceIdentity & { title: string; view?: 'audit' | null }

export type AssistantValue = {
  reference: string
  kind: 'date' | 'timestamp' | 'bytes' | 'text'
  value: string
}

export type AssistantMessage = {
  id: string
  role: 'user' | 'assistant'
  text: string
  sources: AssistantSource[]
  values?: AssistantValue[]
  state: 'complete' | 'streaming' | 'stopped' | 'error'
}

export type AssistantRequest = {
  messages: { role: 'user' | 'assistant'; text: string }[]
  context?: AssistantPageContext | null
  timeZone?: string
}

export type AssistantEvent = {
  type: 'text' | 'status' | 'source' | 'value' | 'heartbeat' | 'done' | 'error'
  text?: string | null
  code?: string | null
  source?: AssistantSource | null
  value?: AssistantValue | null
}

export type AssistantSettings = {
  maxMessages: number
  maxMessageCharacters: number
  maxInputBytes: number
  maxOutputCharacters: number
  provider: string | null
  model: string | null
}
