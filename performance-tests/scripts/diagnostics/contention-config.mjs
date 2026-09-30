export function seconds(value) {
  if (typeof value !== 'string' || !/^(?:\d+(?:\.\d+)?(?:ms|s|m|h))+$/.test(value)) throw new Error('Invalid contention duration.');
  const duration = [...value.matchAll(/(\d+(?:\.\d+)?)(ms|s|m|h)/g)]
    .reduce((sum, match) => sum + Number(match[1]) * { ms: .001, s: 1, m: 60, h: 3600 }[match[2]], 0);
  if (!Number.isFinite(duration) || duration <= 0 || duration > 14400) throw new Error('Contention durations must be >0 and <=4h.');
  return duration;
}

export function contentionConfig(env) {
  const mode = env.NGB_CONTENTION_MODE || 'overlap';
  if (!['overlap', 'control', 'reports'].includes(mode)) throw new Error('NGB_CONTENTION_MODE must be overlap, control or reports.');
  const vus = Number(env.NGB_CAPACITY_VUS || (mode === 'reports' ? '800' : '320'));
  if (!Number.isSafeInteger(vus) || vus < 1 || vus > 5000) throw new Error('Contention requires one fixed NGB_CAPACITY_VUS target (1..5000).');
  const ramp = env.NGB_CAPACITY_RAMP_DURATION || '5m';
  const hold = env.NGB_CAPACITY_HOLD_DURATION || '20m';
  const down = env.NGB_CAPACITY_RAMP_DOWN_DURATION || '5m';
  const trigger = env.NGB_CONTENTION_TRIGGER_AFTER || '10m';
  const rampSeconds = seconds(ramp), holdSeconds = seconds(hold), downSeconds = seconds(down), triggerSeconds = seconds(trigger);
  if (mode === 'overlap' && (triggerSeconds <= rampSeconds || triggerSeconds >= rampSeconds + holdSeconds)) {
    throw new Error('Finalization must start strictly inside the fixed-load hold.');
  }
  return { mode, vus, ramp, hold, down, triggerSeconds, rampSeconds, holdSeconds,
    durationSeconds: rampSeconds + holdSeconds + downSeconds };
}
