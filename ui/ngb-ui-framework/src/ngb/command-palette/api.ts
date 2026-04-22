import { httpPost } from '../api/http'
import type { CommandPaletteSearchRequestDto, CommandPaletteSearchResponseDto } from './types'

export async function searchCommandPalette(
  request: CommandPaletteSearchRequestDto,
  signal?: AbortSignal,
): Promise<CommandPaletteSearchResponseDto> {
  return await httpPost<CommandPaletteSearchResponseDto>('/api/search/command-palette', request, { signal })
}

