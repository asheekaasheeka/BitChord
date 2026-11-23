import { describe, expect, it } from 'vitest';
import { cleanBands, EQ_PRESETS, MasterEffects, matchingPreset, preampDb, sectionGainDb, softClip } from './equalizer';

describe('equalizer', () => {
  it('a bell reaches its gain at the centre, a shelf far past its corner', () => {
    expect(sectionGainDb('bell', 1000, 6, 1, 1000)).toBeCloseTo(6, 3);
    expect(sectionGainDb('lowshelf', 60, 6, Math.SQRT1_2, 5)).toBeCloseTo(6, 1);
    expect(sectionGainDb('highshelf', 14000, -5, Math.SQRT1_2, 20000)).toBeLessThan(-3);
  });

  it('takes back half the summed peak boost, never boosts', () => {
    expect(preampDb(EQ_PRESETS.Flat)).toBeCloseTo(0, 6);
    expect(preampDb(EQ_PRESETS['Bass Cut'])).toBeCloseTo(0, 6);
    // The +6 dB low shelf peaks at the bottom of the band, where the 150 Hz bell adds next to nothing.
    expect(preampDb(EQ_PRESETS['Bass Boost'])).toBeCloseTo(-3, 1);
    // Two neighbours at +6 dB sum past either one alone.
    expect(preampDb([0, 0, 6, 6, 0, 0, 0])).toBeLessThan(-3.2);
  });

  it('names presets, and Custom once a slider moves', () => {
    expect(matchingPreset([...EQ_PRESETS.Rock])).toBe('Rock');
    expect(matchingPreset([4, 2.5, -1, -1.5, 1, 3, 4])).toBe('Custom');
  });

  it('cleans stored bands', () => {
    expect(cleanBands(undefined)).toEqual([0, 0, 0, 0, 0, 0, 0]);
    expect(cleanBands([20, -20, 'x', 3])).toEqual([12, -12, 0, 3, 0, 0, 0]);
  });

  it('wires the effects with no loop, and bypasses them when both are off', () => {
    const edges = new Set<string>();
    let n = 0;
    class Node {
      readonly id = `n${n++}`;
      gain = { setTargetAtTime() {} };
      connect(to: Node) {
        edges.add(`${this.id}>${to.id}`);
        return to;
      }
      disconnect() {
        for (const e of [...edges]) if (e.startsWith(`${this.id}>`)) edges.delete(e);
      }
    }
    for (const name of ['BiquadFilterNode', 'GainNode', 'WaveShaperNode', 'ChannelSplitterNode', 'ChannelMergerNode', 'DelayNode', 'IIRFilterNode'])
      Object.assign(globalThis, { [name]: Node });
    const from = new Node();
    const to = new Node();
    const fx = new MasterEffects({ currentTime: 0, sampleRate: 48_000 } as unknown as BaseAudioContext, from as never, to as never);
    expect([...edges].some((e) => e.split('>')[0] === e.split('>')[1])).toBe(false);
    expect(edges.has(`${from.id}>${to.id}`)).toBe(true);

    for (const [eqOn, spatialOn] of [[true, false], [false, true]] as const) {
      fx.set(eqOn, [...EQ_PRESETS.Rock], spatialOn);
      expect(edges.has(`${from.id}>${to.id}`)).toBe(false);
      expect([...edges].filter((e) => e.startsWith(`${from.id}>`))).toHaveLength(1);
    }
    fx.set(false, [...EQ_PRESETS.Rock], false);
    expect(edges.has(`${from.id}>${to.id}`)).toBe(true);
  });

  it('soft clip is linear below the knee and stays inside ±1', () => {
    expect(softClip(0.5)).toBe(0.5);
    expect(softClip(-0.9)).toBe(-0.9);
    expect(softClip(2)).toBeLessThan(1);
    expect(softClip(-2)).toBeGreaterThan(-1);
  });
});
