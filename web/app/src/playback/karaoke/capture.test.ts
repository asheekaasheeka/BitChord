import { describe, expect, it } from 'vitest';
import { TrackCapture } from './capture';

const seg = (stream: string, startSec: number | null, endSec: number | null, ...bytes: number[]) => ({
  stream,
  startSec,
  endSec,
  bytes: Uint8Array.from(bytes),
});

describe('TrackCapture', () => {
  it('joins init and media in time order once the whole track is there', () => {
    const c = new TrackCapture('v');
    c.add(seg('a', 10, 20, 3));
    c.add(seg('a', null, null, 1));
    expect(c.file(20)).toBeNull(); // 0–10 missing
    expect(c.coverage(20)).toBe(0);
    c.add(seg('a', 0, 10, 2));
    expect(c.coverage(20)).toBe(1);
    expect([...c.file(20)!]).toEqual([1, 2, 3]);
  });

  it('keeps one copy of a refetched segment and needs an init segment', () => {
    const c = new TrackCapture('v');
    c.add(seg('a', 0, 10, 2));
    c.add(seg('a', 0, 10, 9));
    expect(c.file(10)).toBeNull();
    c.add(seg('a', null, null, 1));
    expect([...c.file(10)!]).toEqual([1, 9]);
  });

  it('does not mix streams and picks the one that reaches furthest', () => {
    const c = new TrackCapture('v');
    c.add(seg('aac', null, null, 7));
    c.add(seg('aac', 0, 5, 8));
    c.add(seg('opus', null, null, 1));
    c.add(seg('opus', 0, 10, 2));
    c.add(seg('opus', 10, 20, 3));
    expect([...c.file(20)!]).toEqual([1, 2, 3]);
  });

  it('accepts a last segment slightly short of the stated duration', () => {
    const c = new TrackCapture('v');
    c.add(seg('a', null, null, 1));
    c.add(seg('a', 0, 199.4, 2));
    expect(c.file(200)).not.toBeNull();
    expect(c.coverage(200)).toBeCloseTo(0.997, 3);
  });
});
