import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { writeFileSync, readFileSync } from 'node:fs';
import { platform, arch, cpus, totalmem } from 'node:os';
const [testFile, outputMode, summaryPath] = process.argv.slice(2);
const command = (name,args) => { try { return execFileSync(name,args,{encoding:'utf8',stdio:['ignore','pipe','ignore']}).trim(); } catch { return null; } };
const hash = value => createHash('sha256').update(value).digest('hex');
const settings = Object.fromEntries(Object.entries(process.env).filter(([key]) =>
 !/(TOKEN|SECRET|PASSWORD|AUTHORIZATION|CREDENTIAL|KEY)/i.test(key)
 && (/^NGB_(PERF_|PM_|K6_|CAPACITY_|BREAKPOINT_)/.test(key)
 || ['NGB_API_BASE_URL','NGB_VERTICAL'].includes(key))).sort(([a],[b]) => a.localeCompare(b)));
const manifest = {
 schemaVersion:1, startedAtUtc:new Date().toISOString(), testFile, outputMode,
 gitCommit:command('git',['rev-parse','HEAD']),
 trackedDiffSha256:hash(command('git',['diff','HEAD','--']) ?? ''),
 testFileSha256:hash(readFileSync(testFile)),
 changedFiles:command('git',['status','--short']),
 k6Version:command('k6',['version']), nodeVersion:process.version,
 host:{platform:platform(),arch:arch(),logicalCpus:cpus().length,memoryBytes:totalmem()},
 settings,
 datasetId:process.env.NGB_PERF_DATASET_ID || null,
 apiImageId:process.env.NGB_PERF_API_IMAGE_ID || null,
 note:'Host resources are not container limits. Missing dataset/image identities mean comparability is not established.',
};
writeFileSync(summaryPath.replace(/\.json$/, '') + '.manifest.json',JSON.stringify(manifest,null,2)+'\n',{mode:0o600});
