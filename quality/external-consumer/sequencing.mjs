export async function startAfterSuccessfulMigration(migrate, start) {
  await migrate()
  return await start()
}
