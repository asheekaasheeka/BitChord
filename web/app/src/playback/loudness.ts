/**
 * Loudness normalization from YouTube's own figure (player_config.audio_config.loudness_db), as the
 * phone does (PlaybackService.loudnessGainMb): turn a track down by however much louder than
 * YouTube's reference it is.
 *
 * shortcut: cuts only, never boosts (the phone allows +3 dB behind its PeakLimiter); add a limiter
 * on the deck path before allowing a boost.
 */
const MIN_DB = -15;
const MAX_DB = 0;

/** Linear gain for a track's loudness figure; 1 when unknown. */
export function loudnessGain(loudnessDb: number | null | undefined): number {
  if (loudnessDb == null || !Number.isFinite(loudnessDb)) return 1;
  return 10 ** (Math.min(MAX_DB, Math.max(MIN_DB, -loudnessDb)) / 20);
}
