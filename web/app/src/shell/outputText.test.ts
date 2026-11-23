import { describe, expect, it } from 'vitest';
import { describeOutput } from './outputText';

describe('describeOutput', () => {
  it('says when the source is resampled to the device rate', () => {
    expect(describeOutput(44_100, { webAudio: true, sampleRateHz: 48_000 })).toBe(
      'Output · Web Audio (float32) · 48 kHz · resampled from 44.1 kHz',
    );
    expect(describeOutput(48_000, { webAudio: false, sampleRateHz: 48_000 })).toBe('Output · Chromium · 48 kHz · no resampling');
  });

  it('leaves out what it does not know', () => {
    expect(describeOutput(null, { webAudio: false, sampleRateHz: 48_000 })).toBe('Output · Chromium · 48 kHz');
    expect(describeOutput(96_000, { webAudio: false, sampleRateHz: null })).toBe('Output · Chromium');
  });
});
