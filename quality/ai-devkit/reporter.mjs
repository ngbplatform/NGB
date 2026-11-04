export default async function* reporter(events) {
  for await (const event of events) {
    if (event.type === 'test:summary') yield `${JSON.stringify(event)}\n`
    if (event.type === 'test:fail') {
      yield `${JSON.stringify({ type: event.type, data: { name: event.data.name, file: event.data.file,
        message: event.data.details?.error?.message } })}\n`
    }
    if (event.type === 'test:coverage') {
      const files = event.data.summary.files.map(file => ({
        path: file.path,
        totalLineCount: file.totalLineCount,
        coveredLineCount: file.coveredLineCount,
        totalBranchCount: file.totalBranchCount,
        coveredBranchCount: file.coveredBranchCount,
        totalFunctionCount: file.totalFunctionCount,
        coveredFunctionCount: file.coveredFunctionCount,
      }))
      yield `${JSON.stringify({ type: event.type, files })}\n`
    }
  }
}
