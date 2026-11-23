import { describe, expect, it } from 'vitest';
import { loudnessGain } from './loudness';
import { spatialGains } from './spatial';

describe('loudness normalization', () => {
  it('turns loud tracks down, leaves quiet and unknown ones alone', () => {
    expect(loudnessGain(6)).toBeCloseTo(10 ** (-6 / 20), 6);
    expect(loudnessGain(-4)).toBe(1);
    expect(loudnessGain(null)).toBe(1);
    expect(loudnessGain(NaN)).toBe(1);
    expect(loudnessGain(40)).toBeCloseTo(10 ** (-15 / 20), 6);
  });
});

describe('spatial', () => {
  it('is the identity when off', () => {
    expect(spatialGains(false)).toEqual({ same: 1, cross: 0, feed: 0 });
  });

  it('keeps the mid level and widens the side, as the phone does', () => {
    const { same, cross } = spatialGains(true);
    // L = R (mono content): mid is scaled by the make-up gain only.
    expect(same + cross).toBeCloseTo(0.82, 6);
    // L = −R (pure side): widened 2.5×, then the make-up.
    expect(same - cross).toBeCloseTo(0.82 * 2.5, 6);
  });
});
