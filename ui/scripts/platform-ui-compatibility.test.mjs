import assert from 'node:assert/strict'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import ts from 'typescript'
import { compareCssTokens, comparePackageContracts, compareTypeContracts } from './platform-ui-compatibility.mjs'

test('package conditions, CSS and peer compatibility reject breaks and accept additions', () => {
  const original = { exports: { '.': { types: './index.ts', import: './index.ts' } }, peerDependencies: { vue: '^3.5.0' } }
  assert.deepEqual(comparePackageContracts(original, original), [])
  assert.match(comparePackageContracts(original, { exports: {}, peerDependencies: {} }).join(), /Removed entry point/)
  const next = structuredClone(original)
  next.exports['.'].types = './changed.ts'
  next.peerDependencies.vue = '^3.6.0'
  next.peerDependencies.tailwindcss = '^3.4.17'
  assert.equal(comparePackageContracts(original, next).length, 3)
  next.exports['.'] = original.exports['.']
  next.peerDependencies.vue = '^3.0.0'
  next.peerDependenciesMeta = { tailwindcss: { optional: true } }
  next.exports['./tailwind-preset'] = './tailwind-preset.js'
  assert.deepEqual(comparePackageContracts(original, next), [])
  assert.deepEqual(compareCssTokens(':root { --ngb-bg: white; --ngb-text: black; }', ':root { --ngb-bg: white; }'), ['Removed CSS token: --ngb-text'])
  assert.deepEqual(compareCssTokens('--ngb-bg: white;', '--ngb-bg: black; --ngb-new: red;'), [])
})

test('declaration comparison detects type, signature, generics and Vue contract breaks', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-ui-types-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const previous = join(root, 'previous.ts')
  const candidate = join(root, 'candidate.ts')
  const cases = [
    ['export interface Payload { name: string }', 'export interface Payload { name: number }', true],
    ['export interface Payload { name: string }', 'export interface Payload { name: string; extra?: boolean }', false],
    ['export interface Payload { name?: string }', 'export interface Payload { name: string }', true],
    ['export declare function run(value: string): string', 'export declare function run(value: number): number', true],
    ['export declare function run(value: string): string', 'export declare function run(value: string | number): string', false],
    ['export type Box<T> = { value: T }', 'export type Box<T extends string> = { value: T }', true],
    ['export type Box<T extends string | number> = T[]', 'export type Box<T extends string> = T[]', true],
    ['export type Box<T extends string> = T[]', 'export type Box<T extends string | number> = T[]', false],
    ['export type Box<T = string> = T[]', 'export type Box<T> = T[]', true],
    ['export type Box<T> = T[]', 'export type Box<T, U> = T[]', true],
    ['export type Box<T> = T[]', 'export type Box = string[]', true],
    ['export type Box<T> = T[]', 'export type Box<T, U = string> = T[]', false],
    ['export declare const Component: new () => { $props: { title?: string } }', 'export declare const Component: new () => { $props: { title: number } }', true],
    ['export declare const Component: new () => { $props: { title?: string } }', 'export declare const Component: new () => { $props: {} }', true],
    ['export declare const Component: new () => { $props: { title?: string } }', 'export declare const Component: new () => { $props: { title?: string; extra?: boolean } }', false],
    ['export declare const Component: new () => { $emit: (event: "save", value: string) => void }', 'export declare const Component: new () => { $emit: (event: "save", value: number) => void }', true],
    ['export declare const Component: new () => { $slots: { default: (value: string) => string } }', 'export declare const Component: new () => { $slots: {} }', true],
    ['export const removed = 1', 'export const added = 1', true],
    ['const value = 1; export { value as named }', 'const value = 1; export { value as named }; export const added = 2', false],
  ]
  for (const [oldSource, newSource, breaks] of cases) {
    await writeFile(previous, oldSource)
    await writeFile(candidate, newSource)
    const program = ts.createProgram([previous, candidate], { strict: true, noEmit: true, skipLibCheck: true })
    const diagnostics = ts.getPreEmitDiagnostics(program)
    assert.equal(diagnostics.length, 0, ts.formatDiagnostics(diagnostics, { getCanonicalFileName: path => path, getCurrentDirectory: () => root, getNewLine: () => '\n' }))
    assert.equal(compareTypeContracts(program, previous, candidate).length > 0, breaks, `${oldSource} -> ${newSource}`)
  }
  const program = ts.createProgram([previous, candidate], { strict: true })
  assert.throws(() => compareTypeContracts(program, previous, join(root, 'missing.ts')), /Missing/)
  await writeFile(candidate, 'const plain = 1')
  assert.throws(() => compareTypeContracts(ts.createProgram([previous, candidate], {}), previous, candidate), /not a module/)
})
