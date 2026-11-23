/**
 * Stats for nerds' output line: what the app does to the decoded audio, kept apart from the source
 * line above it. Ends at the app: Windows' shared mixer and the device format come after and aren't read.
 */
export function describeOutput(sourceHz: number | null | undefined, out: { webAudio: boolean; sampleRateHz: number | null }): string {
  const khz = (hz: number) => `${+(hz / 1000).toFixed(1)} kHz`;
  const path = out.webAudio ? 'Web Audio (float32)' : 'Chromium';
  if (!out.sampleRateHz) return `Output · ${path}`;
  const rate = khz(out.sampleRateHz);
  if (!sourceHz) return `Output · ${path} · ${rate}`;
  return sourceHz === out.sampleRateHz
    ? `Output · ${path} · ${rate} · no resampling`
    : `Output · ${path} · ${rate} · resampled from ${khz(sourceHz)}`;
}
