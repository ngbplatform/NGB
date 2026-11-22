import { useAccessStore } from '../security/useAccessStore'

export function useGeneralJournalEntryPermissions() {
  const access = useAccessStore()
  void access.load()

  return (action: string) => access.current?.isAuthenticated === true && access.current.isActive
    && (access.current.isBootstrapAdmin || access.hasPermission(`document.general_journal_entry.${action}`))
}
