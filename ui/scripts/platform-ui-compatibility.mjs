import ts from 'typescript'
import semver from 'semver'

export function comparePackageContracts(previous, candidate) {
  const errors = []
  for (const [name, contract] of Object.entries(previous.exports)) {
    const replacement = candidate.exports[name]
    if (!replacement) {
      errors.push(`Removed entry point: ${name}`)
    } else if (JSON.stringify(contract) !== JSON.stringify(replacement)) {
      errors.push(`Changed entry-point conditions or targets: ${name}`)
    }
  }
  for (const [name, range] of Object.entries(previous.peerDependencies)) {
    if (!candidate.peerDependencies[name] || !semver.subset(range, candidate.peerDependencies[name])) {
      errors.push(`Narrowed or removed peer range: ${name}`)
    }
  }
  for (const name of Object.keys(candidate.peerDependencies)) {
    if (!previous.peerDependencies[name] && !candidate.peerDependenciesMeta?.[name]?.optional) {
      errors.push(`Added required peer: ${name}`)
    }
  }
  return errors
}

export function compareCssTokens(previousCss, candidateCss) {
  const tokens = css => new Set([...css.matchAll(/(--ngb-[\w-]+)\s*:/g)].map(match => match[1]))
  const candidate = tokens(candidateCss)
  return [...tokens(previousCss)].filter(token => !candidate.has(token))
    .map(token => `Removed CSS token: ${token}`)
}

/** Compare declaration output from vue-tsc, including component constructors,
 * props, emits and slots. Plain export-name snapshots cannot check these contracts.
 */
export function compareTypeContracts(program, previousFile, candidateFile) {
  const checker = program.getTypeChecker()
  const moduleExports = filename => {
    const source = program.getSourceFile(filename)
    if (!source) throw new Error(`Missing declaration entry point: ${filename}`)
    const symbol = checker.getSymbolAtLocation(source)
    if (!symbol) throw new Error(`Entry point is not a module: ${filename}`)
    return checker.getExportsOfModule(symbol)
  }
  const resolveAlias = symbol => symbol.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(symbol) : symbol
  const candidateExports = new Map(moduleExports(candidateFile).map(symbol => [symbol.name, resolveAlias(symbol)]))
  const errors = []
  const checks = [
    `import type * as Previous from ${JSON.stringify(previousFile)}`,
    `import type * as Candidate from ${JSON.stringify(candidateFile)}`,
    'type Accepts<Provided extends Required, Required> = Provided',
    'type Props<T> = T extends abstract new (...args: any[]) => { $props: infer P } ? P : never',
    'type Slots<T> = T extends abstract new (...args: any[]) => { $slots: infer S } ? S : never',
  ]
  let index = 0
  for (const exported of moduleExports(previousFile)) {
    const previous = resolveAlias(exported)
    const candidate = candidateExports.get(exported.name)
    const name = exported.name
    if (!candidate) {
      errors.push(`Removed public export: ${name}`)
      continue
    }
    if (previous.flags & ts.SymbolFlags.Value) {
      checks.push(`type Value${index} = Accepts<typeof Candidate.${name}, typeof Previous.${name}>`)
      checks.push(`type Props${index} = Accepts<Props<typeof Previous.${name}>, Props<typeof Candidate.${name}>>`)
      checks.push(`type Slots${index} = Accepts<Slots<typeof Previous.${name}>, Slots<typeof Candidate.${name}>>`)
      checks.push(`type PropKeys${index} = Accepts<keyof Props<typeof Previous.${name}>, keyof Props<typeof Candidate.${name}>>`)
      checks.push(`type SlotKeys${index} = Accepts<keyof Slots<typeof Previous.${name}>, keyof Slots<typeof Candidate.${name}>>`)
    }
    if (previous.flags & ts.SymbolFlags.Type) {
      const parameters = symbol => symbol.declarations.flatMap(declaration => [...declaration.typeParameters ?? []])
      const oldParameters = parameters(previous)
      const newParameters = parameters(candidate)
      if (newParameters.length < oldParameters.length
        || newParameters.slice(oldParameters.length).some(parameter => !parameter.default)) {
        errors.push(`Changed generic arity: ${name}`)
      }
      oldParameters.forEach((parameter, parameterIndex) => {
        const replacement = newParameters[parameterIndex]
        if (replacement && parameter.default && !replacement.default) {
          errors.push(`Removed generic default: ${name}`)
        }
      })
      const format = node => checker.typeToString(checker.getTypeFromTypeNode(node), undefined,
        ts.TypeFormatFlags.NoTruncation | ts.TypeFormatFlags.UseFullyQualifiedType)
      const genericParameters = oldParameters.map(parameter => parameter.name.text
        + (parameter.constraint ? ` extends ${format(parameter.constraint)}` : '')
        + (parameter.default ? ` = ${format(parameter.default)}` : ''))
      const declaration = genericParameters.length ? `<${genericParameters.join(', ')}>` : ''
      const argumentsList = oldParameters.length ? `<${oldParameters.map(parameter => parameter.name.text).join(', ')}>` : ''
      const oldType = `Previous.${name}${argumentsList}`
      const newType = `Candidate.${name}${argumentsList}`
      checks.push(`type Input${index}${declaration} = Accepts<${oldType}, ${newType}>`)
      checks.push(`type Output${index}${declaration} = Accepts<${newType}, ${oldType}>`)
      checks.push(`type Keys${index}${declaration} = Accepts<keyof ${oldType}, keyof ${newType}>`)
    }
    index += 1
  }
  const filename = `${candidateFile}.compatibility.ts`
  const options = { ...program.getCompilerOptions(), strict: true, noEmit: true }
  const host = ts.createCompilerHost(options)
  const originalSource = host.getSourceFile.bind(host)
  host.getSourceFile = (path, languageVersion, onError, shouldCreateNewSourceFile) => path === filename
    ? ts.createSourceFile(path, checks.join('\n'), languageVersion, true)
    : originalSource(path, languageVersion, onError, shouldCreateNewSourceFile)
  const validation = ts.createProgram([...program.getRootFileNames(), filename], options, host)
  for (const diagnostic of ts.getPreEmitDiagnostics(validation)) {
    errors.push(ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'))
  }
  return errors
}
