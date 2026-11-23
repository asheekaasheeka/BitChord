import type { AudioSegment } from '@/playback/youtube/engine';

/** Gaps up to this between one segment's end and the next one's start still count as joined. */
const JOIN_SEC = 0.05;
/** The last segment may end this short of the track's duration (metadata rounds it). */
const END_SLACK_SEC = 1;

interface Stream {
  init: Uint8Array | null;
  /** By start time; a refetch (after a seek) replaces the copy. */
  media: Map<number, { endSec: number; bytes: Uint8Array }>;
}

/**
 * The segments Shaka downloaded for one track, by stream (a codec switch starts a new one).
 * Init plus every media segment in time order is a playable file again (WebM or fragmented MP4),
 * which decodeAudioData reads.
 */
export class TrackCapture {
  private streams = new Map<string, Stream>();

  constructor(readonly videoId: string) {}

  add(segment: AudioSegment): void {
    let s = this.streams.get(segment.stream);
    if (!s) this.streams.set(segment.stream, (s = { init: null, media: new Map() }));
    if (segment.startSec === null) s.init = segment.bytes;
    else s.media.set(segment.startSec, { endSec: segment.endSec ?? segment.startSec, bytes: segment.bytes });
  }

  /** How much of [0, durationSec] the best stream covers without a gap from the start, 0–1. */
  coverage(durationSec: number): number {
    const best = this.best(durationSec);
    return best ? Math.min(1, best.reach / durationSec) : 0;
  }

  /** The whole track as one file, or null while a part is still missing. */
  file(durationSec: number): Uint8Array | null {
    const best = this.best(durationSec);
    if (!best || best.reach < durationSec - END_SLACK_SEC) return null;
    const parts = [best.stream.init!, ...best.ordered.map((m) => m.bytes)];
    const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
    let at = 0;
    for (const p of parts) {
      out.set(p, at);
      at += p.length;
    }
    return out;
  }

  /** The stream with an init segment that reaches furthest from 0 without a gap. */
  private best(durationSec: number) {
    let best: { stream: Stream; reach: number; ordered: { endSec: number; bytes: Uint8Array }[] } | null = null;
    for (const stream of this.streams.values()) {
      if (!stream.init) continue;
      const starts = [...stream.media.keys()].sort((a, b) => a - b);
      const ordered: { endSec: number; bytes: Uint8Array }[] = [];
      let reach = 0;
      for (const start of starts) {
        if (start > reach + JOIN_SEC) break;
        const m = stream.media.get(start)!;
        if (m.endSec <= reach) continue; // inside what is already covered
        ordered.push(m);
        reach = m.endSec;
        if (reach >= durationSec) break;
      }
      if (!best || reach > best.reach) best = { stream, reach, ordered };
    }
    return best;
  }
}
