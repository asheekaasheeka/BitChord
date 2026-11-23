import { create } from 'zustand';
import { karaokeRoute, setSegmentSink, usePlayer } from '@/state/player';
import { trackLog } from '@/playback/trackLog';
import { KARAOKE_MAX_SEC, KARAOKE_SAMPLE_RATE, KaraokeError, karaokeSupported, separate } from './bridge';
import { TrackCapture } from './capture';
import { StemPlayer, interleave, toBuffer } from './stems';

export { karaokeSupported };

export type KaraokeStatus = 'off' | 'loading' | 'downloading' | 'separating' | 'on' | 'error';

interface KaraokeState {
  status: KaraokeStatus;
  /** 0–1 within the current status. */
  progress: number;
  /** The lead vocal's level over the instrumental, 0 (karaoke) – 1 (the original). */
  vocal: number;
  message: string | null;
}

export const useKaraoke = create<KaraokeState>()(() => ({ status: 'off', progress: 0, vocal: 0, message: null }));

/** A song's segments stop arriving for this long: it is not going to be whole. */
const STALL_MS = 30_000;
const POLL_MS = 400;
/** How long a new song may take to load before karaoke gives up on it. */
const LOAD_WAIT_MS = 60_000;
/** Captures kept: both decks' tracks and the one before. */
const KEEP = 3;

const captures = new Map<string, TrackCapture>();
let enabled = false;
let run: { videoId: string; abort: AbortController } | null = null;
let playing: { stems: StemPlayer; reconnect: () => void } | null = null;

/** Starts collecting segments; call once at start-up inside the Windows app. */
export function installKaraoke(): void {
  if (!karaokeSupported) return;
  setSegmentSink((videoId, segment) => {
    let c = captures.get(videoId);
    if (!c) {
      captures.set(videoId, (c = new TrackCapture(videoId)));
      while (captures.size > KEEP) captures.delete(captures.keys().next().value!);
    }
    c.add(segment);
  });
  usePlayer.subscribe((s, prev) => {
    if (!enabled) return;
    if (s.automix && !prev.automix) return setKaraoke(false); // AutoMix moves the elements under the stems
    if (s.nowPlaying?.videoId !== prev.nowPlaying?.videoId) void start();
  });
}

export function setKaraoke(on: boolean): void {
  if (on === enabled) return;
  enabled = on;
  if (on) void start();
  else {
    stop();
    useKaraoke.setState({ status: 'off', progress: 0, message: null });
  }
}

export function setVocal(level: number): void {
  useKaraoke.setState({ vocal: level });
  playing?.stems.setVocal(level);
}

function stop() {
  run?.abort.abort();
  run = null;
  playing?.stems.dispose();
  playing?.reconnect();
  playing = null;
}

function fail(message: string) {
  stop();
  useKaraoke.setState({ status: 'error', progress: 0, message });
}

const MESSAGES: Record<string, string> = {
  declined: 'Karaoke needs the vocal-removal model. Turn karaoke on again to download it.',
  download: 'The vocal-removal model could not be downloaded. Check the connection and try again.',
  start: 'The vocal remover could not start.',
  separate: 'The vocal remover stopped on this song.',
};

async function start() {
  stop();
  const videoId = usePlayer.getState().nowPlaying?.videoId;
  if (!videoId) return useKaraoke.setState({ status: 'off' });
  const abort = new AbortController();
  run = { videoId, abort };
  const current = () => run?.abort === abort && !abort.signal.aborted;
  useKaraoke.setState({ status: 'loading', progress: 0, message: null });
  try {
    // nowPlaying names the new song as soon as its load starts; until it finishes the front element
    // still holds the old one (its duration too), so wait for the deck that really plays this song.
    let route: Awaited<ReturnType<typeof karaokeRoute>> = null;
    for (const waitUntil = performance.now() + LOAD_WAIT_MS; ; ) {
      route = await karaokeRoute();
      if (!current()) return;
      if (route?.engine.loadedVideoId === videoId && Number.isFinite(route.audio.duration)) break;
      if (performance.now() > waitUntil) return fail(route ? 'This song did not finish loading.' : 'Play a song first.');
      await new Promise((r) => setTimeout(r, POLL_MS));
      if (!current()) return;
    }
    const { audio, engine, ctx } = route;
    const durationSec = audio.duration;
    if (durationSec > KARAOKE_MAX_SEC) return fail('Karaoke works on songs up to 10 minutes.');

    // 1. The whole song, from the segments Shaka downloads (it is told to fetch to the end).
    engine.bufferWholeTrack(true);
    let file: Uint8Array | null = null;
    try {
      let last = -1;
      let lastAt = performance.now();
      for (;;) {
        const capture = captures.get(videoId);
        file = capture?.file(durationSec) ?? null;
        if (file) break;
        const covered = capture?.coverage(durationSec) ?? 0;
        if (covered > last) [last, lastAt] = [covered, performance.now()];
        // shortcut: a song started mid-way (a resumed session) never gets its opening segments; seek to 0 to fill them.
        else if (performance.now() - lastAt > STALL_MS) return fail('Not all of this song could be loaded. Play it from the start and try again.');
        useKaraoke.setState({ progress: covered });
        await new Promise((r) => setTimeout(r, POLL_MS));
        if (!current()) return;
      }
    } finally {
      engine.bufferWholeTrack(false);
    }

    // 2. PCM at the model's rate.
    const decoded = await new OfflineAudioContext(2, 1, KARAOKE_SAMPLE_RATE).decodeAudioData(file.buffer as ArrayBuffer);
    if (!current()) return;
    const mix = interleave(decoded);

    // 3. The vocal remover, in the app.
    useKaraoke.setState({ status: 'separating', progress: 0 });
    const started = performance.now();
    const { instrumental, gpu } = await separate(
      mix,
      (phase, value) => useKaraoke.setState({ status: phase === 'download' ? 'downloading' : 'separating', progress: value }),
      abort.signal,
    );
    if (!current()) return;
    trackLog.i(`karaoke: separated in ${Math.round(performance.now() - started)}ms on ${gpu ? 'GPU' : 'CPU'}`, videoId);

    // 4. Stems in place of the element, which keeps running silently as the clock.
    const inst = toBuffer(ctx, instrumental, KARAOKE_SAMPLE_RATE);
    const vocals = toBuffer(ctx, mix, KARAOKE_SAMPLE_RATE, instrumental);
    // Muted, not unplugged: an element whose node nothing pulls stops advancing (measured in Edge:
    // currentTime stood still), which froze the clock and looped the stems on the same second.
    const mute = new GainNode(ctx, { gain: 0 });
    mute.connect(route.input);
    route.source.disconnect(route.input);
    route.source.connect(mute);
    const stems = new StemPlayer(ctx, audio, inst, vocals, route.input, useKaraoke.getState().vocal);
    playing = {
      stems,
      reconnect: () => {
        route.source.disconnect(mute);
        mute.disconnect();
        route.source.connect(route.input);
      },
    };
    run = null;
    useKaraoke.setState({ status: 'on', progress: 1 });
  } catch (error) {
    if (!current()) return;
    trackLog.w(`karaoke failed: ${String(error)}`, videoId);
    fail(error instanceof KaraokeError ? (MESSAGES[error.message] ?? MESSAGES.separate!) : 'This song could not be prepared for karaoke.');
  }
}
