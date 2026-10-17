export type AssistantPageContext = {
  kind: 'catalog' | 'document' | 'generalJournalEntry' | 'report'
  typeCode: string
  id?: string | null
}

export type AssistantSource = AssistantPageContext & { title: string }

export type AssistantMessage = {
  id: string
  role: 'user' | 'assistant'
  text: string
  sources: AssistantSource[]
  state: 'complete' | 'streaming' | 'stopped' | 'error'
}

export type AssistantRequest = {
  messages: { role: 'user' | 'assistant'; text: string }[]
  context?: AssistantPageContext | null
}

export type AssistantEvent = {
  type: 'text' | 'status' | 'source' | 'heartbeat' | 'done' | 'error'
  text?: string | null
  code?: string | null
  source?: AssistantSource | null
}

export type AssistantSettings = {
  maxMessages: number
  maxMessageCharacters: number
  maxInputBytes: number
  maxOutputCharacters: number
  provider: string | null
  model: string | null
}
