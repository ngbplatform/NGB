import assert from 'node:assert/strict'
import test from 'node:test'

import {
  classifyCoverageExclusion,
  evaluateCoverage,
  mergeCoverage,
  parseCobertura,
} from './verify-backend-full-coverage.mjs'

test('empty discovery cannot pass and combined reports do not invent disjoint branch hits', () => {
  const thresholds = { lines: 100, branches: 100, methods: 100 }
  assert.equal(evaluateCoverage({ requiredFiles: [], excludedFiles: [], reportFiles: new Map(), thresholds }).passed, false)
  const report = (hits, covered = 1, total = 2) => parseCobertura(`<coverage><class name="Example" filename="Example.cs">
    <methods><method name="Run" signature="()"><lines><line number="1" hits="${hits}" /></lines></method></methods>
    <lines><line number="1" hits="${hits}" condition-coverage="50% (${covered}/${total})" /></lines>
    </class></coverage>`, '/app')
  const combined = mergeCoverage([report(0), report(1)])
  assert.equal(combined.get('Example.cs').lines.get(1), 1)
  assert.equal([...combined.get('Example.cs').methods.values()][0], true)
  assert.deepEqual([...combined.get('Example.cs').branches.values()][0], { covered: 1, total: 2 })
  assert.equal(evaluateCoverage({ requiredFiles: ['Example.cs'], excludedFiles: [], reportFiles: combined, thresholds }).passed, false)
  assert.throws(() => mergeCoverage([report(1), report(1, 2, 3)]), /inventory changed/)
  assert.equal(evaluateCoverage({ requiredFiles: ['Example.cs'], excludedFiles: [],
    reportFiles: mergeCoverage([report(0), report(1, 2)]), thresholds }).passed, true)
})

test('Cobertura source roots resolve project-relative filenames without escaping the application', () => {
  const report = (source, file = 'Program.cs') => `<coverage><sources>${source}</sources>
    <class name="Program" filename="${file}"><lines><line number="1" hits="1" /></lines></class></coverage>`
  const root = '<source>/app/Api/</source>'
  assert.ok(parseCobertura(report(root), '/app').has('Api/Program.cs'))
  assert.ok(parseCobertura(report('<source>Api/</source>'), '/app').has('Api/Program.cs'))
  assert.ok(parseCobertura(report(root + root), '/app').has('Api/Program.cs'))
  assert.equal(parseCobertura(report('<source>/outside/</source>'), '/app').size, 0)
  assert.equal(parseCobertura(report(root, '../../outside.cs'), '/app').size, 0)
  assert.throws(() => parseCobertura(report(root + '<source>/app/Worker/</source>'), '/app'), /unambiguous/)
  assert.ok(parseCobertura(report(root + '<source>/app/Worker/</source>', '/app/Api/Program.cs'), '/app').has('Api/Program.cs'))
})

test('declaration-only files are excluded until executable logic appears', () => {
  assert.equal(
    classifyCoverageExclusion('NGB.Core/Status.cs', 'namespace NGB.Core; public enum Status { Open, Closed }'),
    'declaration-only-enum-or-delegate',
  )
  assert.equal(
    classifyCoverageExclusion('NGB.Core/IClock.cs', 'namespace NGB.Core; public interface IClock { DateTime UtcNow { get; } }'),
    'declaration-only-interface',
  )
  assert.equal(
    classifyCoverageExclusion(
      'NGB.Core/IMapper.cs',
      'namespace NGB.Core; public interface IMapper { T Map<T>() where T : class; }',
    ),
    'declaration-only-interface',
  )
  assert.equal(
    classifyCoverageExclusion('NGB.Core/IClock.cs', 'namespace NGB.Core; public interface IClock { DateTime UtcNow => DateTime.UtcNow; }'),
    null,
  )
  assert.equal(
    classifyCoverageExclusion('NGB.Core/Names.cs', 'namespace NGB.Core; public static class Names { public const string Main = "main"; }'),
    'declaration-only-constants',
  )
  assert.equal(
    classifyCoverageExclusion('NGB.Core/Names.cs', 'namespace NGB.Core; public static class Names { public static string Normalize(string value) => value.Trim(); }'),
    null,
  )
})

test('assembly attributes, global usings, and generator declarations are excluded only while declarative', () => {
  assert.equal(
    classifyCoverageExclusion(
      'NGB.Core/InternalsVisibleTo.cs',
      'using System.Runtime.CompilerServices; [assembly: InternalsVisibleTo("NGB.Core.Tests")]',
    ),
    'declaration-only-attributes-or-usings',
  )
  assert.equal(
    classifyCoverageExclusion('NGB.Core/GlobalUsings.cs', 'global using NGB.Runtime.CurrentActor;'),
    'declaration-only-attributes-or-usings',
  )
  assert.equal(
    classifyCoverageExclusion(
      'NGB.Core/RuntimeLog.cs',
      `using Microsoft.Extensions.Logging;
       internal static partial class RuntimeLog {
         [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Started")]
         public static partial void Started(ILogger logger);
       }`,
    ),
    'source-generator-declarations',
  )
  assert.equal(
    classifyCoverageExclusion(
      'NGB.Core/RuntimeLog.cs',
      `internal static partial class RuntimeLog {
         public static void Started(ILogger logger) { logger.LogInformation("Started"); }
       }`,
    ),
    null,
  )
})

test('only declarative IDdlObject migration shapes are excluded', () => {
  const declaration = `
    namespace NGB.PostgreSql.Migrations.Platform;
    public sealed class UsersMigration : IDdlObject
    {
      public string Name => "users";
      public string Generate() => "CREATE TABLE users(id uuid);";
    }
  `
  const logic = declaration.replace(
    'public string Generate() => "CREATE TABLE users(id uuid);";',
    'public string Generate() { if (Enabled) return "CREATE TABLE users(id uuid);"; return ""; }',
  )
  assert.equal(
    classifyCoverageExclusion('NGB.PostgreSql/Migrations/Platform/UsersMigration.cs', declaration),
    'declarative-ddl-migration',
  )
  assert.equal(classifyCoverageExclusion('NGB.PostgreSql/Migrations/Platform/UsersMigration.cs', logic), null)
})

test('Cobertura is evaluated per file, including lines, branches, and methods', () => {
  const xml = `
    <coverage line-rate="0.5" branch-rate="0.5">
      <packages><package><classes>
        <class name="NGB.Core.Calculator" filename="NGB.Core/Calculator.cs">
          <methods>
            <method name="Add" signature="()"><lines><line number="5" hits="2" /></lines></method>
            <method name="Subtract" signature="()"><lines><line number="9" hits="0" /></lines></method>
          </methods>
          <lines>
            <line number="5" hits="2" branch="True" condition-coverage="50% (1/2)" />
            <line number="9" hits="0" branch="False" />
          </lines>
        </class>
      </classes></package></packages>
    </coverage>
  `
  const report = parseCobertura(xml, '/repo')
  const result = evaluateCoverage({
    requiredFiles: ['NGB.Core/Calculator.cs', 'NGB.Core/Missing.cs'],
    excludedFiles: [],
    reportFiles: report,
    thresholds: { lines: 100, branches: 100, methods: 100 },
  })

  assert.equal(result.passed, false)
  assert.deepEqual(result.missingFiles, ['NGB.Core/Missing.cs'])
  assert.equal(result.overall.lines.percentage, 50)
  assert.equal(result.overall.branches.percentage, 50)
  assert.equal(result.overall.methods.percentage, 50)
  assert.deepEqual(result.failingFiles[0].failures, ['lines 50.00%', 'branches 50.00%', 'methods 50.00%'])
})

test('branches from different classes on the same source line are not collapsed', () => {
  const xml = `
    <coverage>
      <packages><package><classes>
        <class name="NGB.Core.Container" filename="NGB.Core/Container.cs">
          <methods><method name="Run" signature="()"><lines><line number="5" hits="1" /></lines></method></methods>
          <lines><line number="5" hits="1" branch="True" condition-coverage="100% (2/2)" /></lines>
        </class>
        <class name="NGB.Core.Container/Nested" filename="NGB.Core/Container.cs">
          <lines><line number="5" hits="0" branch="True" condition-coverage="0% (0/2)" /></lines>
        </class>
      </classes></package></packages>
    </coverage>
  `
  const report = parseCobertura(xml, '/repo')
  const result = evaluateCoverage({
    requiredFiles: ['NGB.Core/Container.cs'],
    excludedFiles: [],
    reportFiles: report,
    thresholds: { lines: 100, branches: 100, methods: 100 },
  })

  assert.equal(result.overall.lines.percentage, 100)
  assert.equal(result.overall.branches.percentage, 50)
  assert.deepEqual(result.failingFiles[0].failures, ['branches 50.00%'])
})

test('an empty class cannot masquerade as complete executable-source coverage', () => {
  const report = parseCobertura('<coverage><class name="Example" filename="Example.cs"></class></coverage>', '/repo')
  const result = evaluateCoverage({
    requiredFiles: ['Example.cs'],
    excludedFiles: [],
    reportFiles: report,
    thresholds: { lines: 100, branches: 100, methods: 100 },
  })

  assert.equal(result.passed, false)
  assert.deepEqual(result.perFile[0].failures, ['no executable lines in report', 'no executable methods in report'])
})
