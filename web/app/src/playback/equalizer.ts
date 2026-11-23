/**
 * The phone's manual equaliser (shared/.../playback/EqualizerCurve.kt and EqualizerPreset.kt):
 * seven sliders, the same presets and the same make-up attenuation, as Web Audio filters on the
 * player's master output.
 */

/** Slider centres; the two ends are shelves, the rest bells. */
export const EQ_BANDS_HZ = [60, 150, 400, 1000, 2500, 6000, 14000] as const;
/** How far a slider travels either way. */
export const EQ_RANGE_DB = 12;
const BELL_Q = 1;
// Web Audio shelves ignore Q and always have slope S = 1, which is Q = 1/√2.
const SHELF_Q = Math.SQRT1_2;
/** How much of the curve's peak boost the preamp takes back; the soft clip covers the rest. */
const MAKEUP_FRACTION = 0.5;
const RESPONSE_POINTS = 96;

export const EQ_PRESETS = {
  Flat: [0, 0, 0, 0, 0, 0, 0],
  Acoustic: [3, 1.5, 0, 1.5, 2.5, 2, 1],
  'Bass Boost': [6, 4, 1.5, 0, 0, 0, 0],
  'Bass Cut': [-6, -4, -1.5, 0, 0, 0, 0],
  Vocal: [-3, -1.5, 1, 3.5, 3, 1, -1],
  'Treble Boost': [0, 0, 0, 0, 1.5, 3.5, 5],
  'Treble Cut': [0, 0, 0, 0, -1.5, -3.5, -5],
  Loudness: [6, 3.5, 0, -1.5, -1, 2, 5],
  'Spoken Word': [-5, -2.5, 1.5, 4, 3.5, 1.5, -2],
  Electronic: [5, 3, -1, 0, 1, 3, 4],
  Rock: [4, 2.5, -1, -1.5, 1, 3, 3.5],
  'Hip-Hop': [6, 4, 0.5, -1, 0.5, 2, 2.5],
  Jazz: [3, 1.5, 0, 1, 1.5, 2, 2.5],
  Classical: [3, 2, 0, 0, 1, 2.5, 3],
  'Small Speakers': [5, 4, 2, 0.5, 0, -1, -2],
  'Late Night': [3, 1, 0, 1.5, 1, -1, -3],
} as const satisfies Record<string, readonly number[]>;

export type EqPreset = keyof typeof EQ_PRESETS;

/** The preset these bands are, or 'Custom' once a slider no longer matches any. */
export function matchingPreset(bands: readonly number[]): EqPreset | 'Custom' {
  const hit = Object.entries(EQ_PRESETS).find(
    ([, preset]) => preset.length === bands.length && preset.every((db, i) => Math.abs(db - bands[i]!) < 0.05),
  );
  return (hit?.[0] as EqPreset | undefined) ?? 'Custom';
}

/** Any stored value, as seven gains in range (old or hand-edited settings). */
export function cleanBands(value: unknown): number[] {
  const list = Array.isArray(value) ? value : [];
  return EQ_BANDS_HZ.map((_, i) => {
    const db = Number(list[i]);
    return Number.isFinite(db) ? Math.min(EQ_RANGE_DB, Math.max(-EQ_RANGE_DB, db)) : 0;
  });
}

type Kind = 'bell' | 'lowshelf' | 'highshelf';
const kindOf = (band: number): Kind => (band === 0 ? 'lowshelf' : band === EQ_BANDS_HZ.length - 1 ? 'highshelf' : 'bell');

/** One section's analog magnitude at [hz], in dB (EqualizerCurve.kt's sectionGainDb). */
export function sectionGainDb(kind: Kind, centreHz: number, gainDb: number, q: number, hz: number): number {
  if (Math.abs(gainDb) < 0.01) return 0;
  const a = 10 ** (gainDb / 40);
  const a2 = a * a;
  const x2 = (hz / centreHz) ** 2;
  const qq = q * q;
  let magnitude: number;
  if (kind === 'bell') {
    const flat = (1 - x2) ** 2;
    magnitude = Math.sqrt((flat + (x2 * a2) / qq) / (flat + x2 / (a2 * qq)));
  } else {
    const common = (x2 * a) / qq;
    const low = (a - x2) ** 2 + common;
    const high = (1 - a * x2) ** 2 + common;
    magnitude = a * Math.sqrt(kind === 'lowshelf' ? low / high : high / low);
  }
  return 20 * Math.log10(magnitude);
}

/** Make-up attenuation (≤ 0 dB): half the summed curve's peak boost, as on the phone. */
export function preampDb(bands: readonly number[]): number {
  let peak = 0;
  for (let point = 0; point < RESPONSE_POINTS; point++) {
    const hz = 20 * 1000 ** (point / (RESPONSE_POINTS - 1));
    let sum = 0;
    EQ_BANDS_HZ.forEach((centre, i) => {
      const kind = kindOf(i);
      sum += sectionGainDb(kind, centre, bands[i] ?? 0, kind === 'bell' ? BELL_Q : SHELF_Q, hz);
    });
    peak = Math.max(peak, sum);
  }
  return -peak * MAKEUP_FRACTION;
}

/**
 * Soft clip in place of the phone's PeakLimiter: linear below 0.9, then a tanh knee up to 1.
 * Input is pre-scaled by 1/HEADROOM so the curve covers ±HEADROOM instead of clamping at ±1.
 */
const HEADROOM = 2;
const KNEE = 0.9;
export function softClip(x: number): number {
  const m = Math.abs(x);
  if (m <= KNEE) return x;
  return Math.sign(x) * (KNEE + (1 - KNEE) * Math.tanh((m - KNEE) / (1 - KNEE)));
}

/**
 * [from] → seven filters → preamp → soft clip → [to]. Off, [from] goes straight to [to],
 * so the clip never touches audio the user didn't ask to change.
 */
export class MasterEq {
  private readonly input: BiquadFilterNode;
  private readonly filters: BiquadFilterNode[];
  private readonly preamp: GainNode;
  private on = false;

  constructor(
    private readonly ctx: BaseAudioContext,
    private readonly from: AudioNode,
    private readonly to: AudioNode,
  ) {
    this.filters = EQ_BANDS_HZ.map((frequency, i) => {
      const kind = kindOf(i);
      return new BiquadFilterNode(ctx, { type: kind === 'bell' ? 'peaking' : kind, frequency, Q: BELL_Q, gain: 0 });
    });
    // Chain each filter to the next; never a filter to itself (a feedback loop that screams).
    for (let i = 1; i < this.filters.length; i++) this.filters[i - 1]!.connect(this.filters[i]!);
    this.input = this.filters[0]!;
    this.preamp = new GainNode(ctx, { gain: 1 / HEADROOM });
    const curve = new Float32Array(4097);
    for (let i = 0; i < curve.length; i++) curve[i] = softClip(((i / (curve.length - 1)) * 2 - 1) * HEADROOM);
    const shaper = new WaveShaperNode(ctx, { curve });
    this.filters.at(-1)!.connect(this.preamp).connect(shaper).connect(to);
    from.connect(to);
  }

  /** Glides to the new curve, so a slider drag or a preset never clicks. */
  set(on: boolean, bands: readonly number[]): void {
    const now = this.ctx.currentTime;
    const gains = on ? cleanBands(bands) : EQ_BANDS_HZ.map(() => 0);
    this.filters.forEach((f, i) => f.gain.setTargetAtTime(gains[i]!, now, 0.02));
    this.preamp.gain.setTargetAtTime(10 ** (preampDb(gains) / 20) / HEADROOM, now, 0.02);
    if (on === this.on) return;
    this.on = on;
    this.from.disconnect();
    this.from.connect(on ? this.input : this.to);
  }
}
