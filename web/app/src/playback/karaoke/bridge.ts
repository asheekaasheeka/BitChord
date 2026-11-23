/**
 * The Windows app's vocal remover (web/windows: Karaoke/KaraokeBridge.cs), over WebView2's
 * chrome.webview. The song goes over as 44.1 kHz stereo float PCM in a shared buffer and comes
 * back, in the same buffer, as the instrumental.
 */

interface SharedBufferEvent extends Event {
  getBuffer(): ArrayBuffer;
  additionalData: unknown;
}

interface WebViewMessageEvent extends Event {
  data: unknown;
}

interface WebView {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (e: WebViewMessageEvent) => void): void;
  addEventListener(type: 'sharedbufferreceived', listener: (e: SharedBufferEvent) => void): void;
  removeEventListener(type: 'message', listener: (e: WebViewMessageEvent) => void): void;
  removeEventListener(type: 'sharedbufferreceived', listener: (e: SharedBufferEvent) => void): void;
  releaseBuffer(buffer: ArrayBuffer): void;
}

const webview: WebView | null =
  (globalThis as { chrome?: { webview?: WebView } }).chrome?.webview ?? null;

/** True inside the Windows app, the only host with a vocal remover. */
export const karaokeSupported = webview !== null;

export const KARAOKE_SAMPLE_RATE = 44_100;
/** The app refuses longer songs (KaraokeMessage.MaxFrames). */
export const KARAOKE_MAX_SEC = 600;

export type SeparatePhase = 'download' | 'separate';

/** The app's reason: "declined" (model download refused), "download", "start", "separate". */
export class KaraokeError extends Error {}

interface Reply {
  karaoke?: string;
  job?: string;
  phase?: SeparatePhase;
  value?: number;
  message?: string;
  gpu?: boolean;
}

let jobs = 0;

/** Separates interleaved stereo PCM; resolves with the instrumental, same layout and length. */
export function separate(
  pcm: Float32Array,
  onProgress: (phase: SeparatePhase, value: number) => void,
  signal: AbortSignal,
): Promise<{ instrumental: Float32Array; gpu: boolean }> {
  const wv = webview;
  if (!wv) return Promise.reject(new KaraokeError('unsupported'));
  const job = `k${++jobs}`;
  return new Promise((resolve, reject) => {
    let buffer: ArrayBuffer | null = null;
    const finish = () => {
      wv.removeEventListener('message', onMessage);
      wv.removeEventListener('sharedbufferreceived', onBuffer);
      signal.removeEventListener('abort', onAbort);
      if (buffer) wv.releaseBuffer(buffer);
      buffer = null;
    };
    const onMessage = (e: WebViewMessageEvent) => {
      const m = e.data as Reply | null;
      if (!m || m.job !== job) return;
      if (m.karaoke === 'progress' && m.phase && typeof m.value === 'number') onProgress(m.phase, m.value);
      else if (m.karaoke === 'done' && buffer) {
        const instrumental = new Float32Array(buffer).slice();
        wv.postMessage({ karaoke: 'release', job });
        finish();
        resolve({ instrumental, gpu: m.gpu === true });
      } else if (m.karaoke === 'error') {
        finish();
        reject(new KaraokeError(m.message ?? 'separate'));
      }
    };
    const onBuffer = (e: SharedBufferEvent) => {
      if ((e.additionalData as Reply | null)?.job !== job) return;
      buffer = e.getBuffer();
      if (buffer.byteLength !== pcm.byteLength) {
        wv.postMessage({ karaoke: 'cancel', job });
        finish();
        reject(new KaraokeError('start'));
        return;
      }
      new Float32Array(buffer).set(pcm);
      wv.postMessage({ karaoke: 'run', job });
    };
    const onAbort = () => {
      wv.postMessage({ karaoke: 'cancel', job });
      finish();
      reject(new DOMException('Karaoke was turned off', 'AbortError'));
    };
    if (signal.aborted) return onAbort();
    wv.addEventListener('message', onMessage);
    wv.addEventListener('sharedbufferreceived', onBuffer);
    signal.addEventListener('abort', onAbort);
    wv.postMessage({ karaoke: 'separate', job, frames: pcm.length / 2 });
  });
}
