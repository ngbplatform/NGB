import assert from 'node:assert/strict'
import { test } from 'node:test'
import { resolve } from 'node:path'

import {
  isDeclarationOnlySource,
  isVerifiedDeclarationOnlyFeatureFile,
} from './verify-cobertura-diff.mjs'

const repositoryRoot = resolve(import.meta.dirname, '../..')

const declarationOnlyFiles = [
  'NGB.Contracts/Documents/DocumentActionContractEnums.cs',
  'NGB.Contracts/Documents/StandardDocumentTargets.cs',
  'NGB.Contracts/WorkCenter/WorkCenterContractEnums.cs',
  'NGB.Core/Documents/Actions/DocumentActionContextKeys.cs',
  'NGB.Core/Documents/Actions/DocumentActionEnums.cs',
  'NGB.Core/WorkCenter/WorkCenterEnums.cs',
  'NGB.Definitions/Documents/Actions/IDocumentActionDefinitionsContributor.cs',
]

test('the explicit exclusions remain declaration-only', async () => {
  for (const file of declarationOnlyFiles) {
    assert.equal(
      await isVerifiedDeclarationOnlyFeatureFile(file, repositoryRoot),
      true,
      `${file} must contain declarations only`,
    )
  }
})

test('a file outside the explicit exclusions is never skipped', async () => {
  assert.equal(
    await isVerifiedDeclarationOnlyFeatureFile(
      'NGB.Runtime/WorkCenter/WorkCenterQueryService.cs',
      repositoryRoot,
    ),
    false,
  )
})

test('constant containers with executable members are not declaration-only', () => {
  assert.equal(isDeclarationOnlySource(`
    namespace NGB.Core.WorkCenter;
    public static class Values
    {
        public const string Code = "code";
        public static string Resolve() => Code;
    }
  `, 'constants'), false)
})

test('interfaces with default implementations are not declaration-only', () => {
  assert.equal(isDeclarationOnlySource(`
    namespace NGB.Core.WorkCenter;
    public interface IContributor
    {
        void Contribute() { System.Console.WriteLine("implemented"); }
    }
  `, 'interface'), false)
})
