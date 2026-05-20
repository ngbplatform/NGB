import { sleep } from 'k6';

import { randomInt } from './random.ts';

export function thinkTime(minSeconds = 0.3, maxSeconds = 1.2): void {
  const millis = randomInt(Math.round(minSeconds * 1000), Math.round(maxSeconds * 1000));
  sleep(millis / 1000);
}
