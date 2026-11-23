/** Off by more than this, the stems restart at the element's position. */
const DRIFT_SEC = 0.08;
const CHECK_MS = 250;
/** HTMLMediaElement.HAVE_FUTURE_DATA: below it the element is stalled and the stems wait. */
const HAVE_FUTURE_DATA = 3;

/**
 * Plays the instrumental and the lead vocal in step with a (silent) <audio> element, which stays the
 * clock: the lyrics and the seek bar keep reading it. Restarts the stems on every
 * play, seek, rate change or stall, and when they drift.
 */
export class StemPlayer {
  private sources: AudioBufferSourceNode[] = [];
  private startCtx = 0;
  private startPos = 0;
  private rate = 1;
  private readonly out: GainNode;
  private readonly vocal: GainNode;
  private readonly timer: ReturnType<typeof setInterval>;
  private readonly onEvent = () => this.sync();
  private static readonly EVENTS = ['play', 'playing', 'pause', 'seeked', 'ratechange', 'waiting', 'ended'] as const;

  constructor(
    private readonly ctx: AudioContext,
    private readonly audio: HTMLAudioElement,
    private readonly instrumental: AudioBuffer,
    private readonly vocals: AudioBuffer,
    destination: AudioNode,
    vocalLevel: number,
  ) {
    this.out = new GainNode(ctx, { gain: 1 });
    this.vocal = new GainNode(ctx, { gain: vocalLevel });
    this.vocal.connect(this.out);
    this.out.connect(destination);
    for (const e of StemPlayer.EVENTS) audio.addEventListener(e, this.onEvent);
    this.timer = setInterval(() => this.check(), CHECK_MS);
    this.sync();
  }

  setVocal(level: number) {
    this.vocal.gain.setTargetAtTime(level, this.ctx.currentTime, 0.02);
  }

  private stopSources() {
    for (const s of this.sources) {
      try {
        s.stop();
      } catch {
        // never started
      }
      s.disconnect();
    }
    this.sources = [];
  }

  private sync() {
    this.stopSources();
    const a = this.audio;
    if (a.paused || a.ended || a.readyState < HAVE_FUTURE_DATA) return;
    const pos = a.currentTime;
    if (pos >= this.instrumental.duration) return;
    this.rate = a.playbackRate || 1;
    this.startCtx = this.ctx.currentTime;
    this.startPos = pos;
    for (const [buffer, to] of [
      [this.instrumental, this.out],
      [this.vocals, this.vocal],
    ] as const) {
      const s = new AudioBufferSourceNode(this.ctx, { buffer, playbackRate: this.rate });
      s.connect(to);
      s.start(this.startCtx, pos);
      this.sources.push(s);
    }
  }

  private check() {
    const a = this.audio;
    const shouldPlay = !a.paused && !a.ended && a.readyState >= HAVE_FUTURE_DATA;
    if (shouldPlay !== this.sources.length > 0) return this.sync();
    if (!shouldPlay) return;
    const expected = this.startPos + (this.ctx.currentTime - this.startCtx) * this.rate;
    if (Math.abs(expected - a.currentTime) > DRIFT_SEC) this.sync();
  }

  dispose() {
    clearInterval(this.timer);
    for (const e of StemPlayer.EVENTS) this.audio.removeEventListener(e, this.onEvent);
    this.stopSources();
    this.vocal.disconnect();
    this.out.disconnect();
  }
}

/** Interleaved stereo PCM into an AudioBuffer; with [minus], the difference (mix − instrumental = vocals). */
export function toBuffer(ctx: BaseAudioContext, pcm: Float32Array, sampleRate: number, minus?: Float32Array): AudioBuffer {
  const frames = pcm.length / 2;
  const buffer = ctx.createBuffer(2, frames, sampleRate);
  const l = buffer.getChannelData(0);
  const r = buffer.getChannelData(1);
  for (let i = 0; i < frames; i++) {
    l[i] = pcm[2 * i]! - (minus ? minus[2 * i]! : 0);
    r[i] = pcm[2 * i + 1]! - (minus ? minus[2 * i + 1]! : 0);
  }
  return buffer;
}

/** An AudioBuffer as interleaved stereo (mono is doubled). */
export function interleave(buffer: AudioBuffer): Float32Array {
  const l = buffer.getChannelData(0);
  const r = buffer.numberOfChannels > 1 ? buffer.getChannelData(1) : l;
  const out = new Float32Array(buffer.length * 2);
  for (let i = 0; i < buffer.length; i++) {
    out[2 * i] = l[i]!;
    out[2 * i + 1] = r[i]!;
  }
  return out;
}
