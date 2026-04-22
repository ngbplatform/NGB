export type QueryParamValue = string | number | boolean
export type QueryParams = Record<string, QueryParamValue | null | undefined>
export type JsonPrimitive = string | number | boolean | null
export type JsonValue = JsonPrimitive | JsonObject | JsonValue[]
export type JsonObject = { [key: string]: JsonValue }

