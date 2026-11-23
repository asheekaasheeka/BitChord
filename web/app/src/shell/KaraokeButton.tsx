import { Loader2, MicVocal } from 'lucide-react';
import { karaokeSupported, setKaraoke, setVocal, useKaraoke, type KaraokeStatus } from '@/playback/karaoke';
import { usePlayer } from '@/state/player';

const LABELS: Record<KaraokeStatus, (progress: number) => string> = {
  off: () => 'Karaoke',
  loading: (p) => `Karaoke: loading the song ${Math.round(p * 100)}%`,
  downloading: (p) => `Karaoke: downloading the model ${Math.round(p * 100)}%`,
  separating: (p) => `Karaoke: removing the vocal ${Math.round(p * 100)}%`,
  on: () => 'Karaoke on',
  error: () => 'Karaoke',
};

/** Lead vocal off, from the Windows app's vocal remover. Shown only inside the Windows app. */
export function KaraokeButton() {
  const { status, progress, vocal, message } = useKaraoke();
  const hasTrack = usePlayer((s) => s.nowPlaying !== null);
  const automix = usePlayer((s) => s.automix);
  if (!karaokeSupported) return null;
  const busy = status === 'loading' || status === 'downloading' || status === 'separating';
  const label = automix ? 'Karaoke: turn AutoMix off first' : status === 'error' && message ? message : LABELS[status](progress);

  return (
    <div className="flex items-center gap-1">
      {status === 'on' && (
        <input
          type="range"
          min={0}
          max={1}
          step={0.05}
          value={vocal}
          onChange={(e) => setVocal(Number(e.currentTarget.value))}
          aria-label="Guide vocal level"
          title={`Guide vocal ${Math.round(vocal * 100)}%`}
          className="w-16 accent-current"
        />
      )}
      <button
        className="grid size-7 place-items-center rounded-[8px] text-secondary hover:bg-quinary aria-pressed:text-primary aria-pressed:bg-quinary disabled:opacity-40"
        aria-label={label}
        aria-pressed={status !== 'off'}
        title={label}
        disabled={!hasTrack || automix}
        // On an error karaoke is still on (the next song tries again); a click turns it off.
        onClick={() => setKaraoke(status === 'off')}
      >
        {busy ? <Loader2 size={16} className="animate-spin" /> : <MicVocal size={16} className={status === 'error' ? 'text-red-500' : undefined} />}
      </button>
    </div>
  );
}
