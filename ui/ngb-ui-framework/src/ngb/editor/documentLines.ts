export type DocumentLinesColumn = {
  key: string
  label: string
  width?: string
  align?: 'left' | 'center' | 'right'
}

export type DocumentLinesErrors = Record<number, Record<string, string | readonly string[] | null | undefined>>

export type DocumentLinesHandle = {
  focusCell: (rowIndex: number, fieldKey: string) => boolean
  focusFirstError: () => boolean
}
