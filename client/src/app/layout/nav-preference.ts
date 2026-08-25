/**
 * Whether the sidebar is collapsed to an icon rail.
 *
 * Lives outside the shell because two screens now set it — the rail's own toggle button and the
 * Settings page — and two copies of the same storage key is how they drift apart. The shell reads
 * it once at construction, so a change made in Settings shows on the next navigation.
 *
 * Every access is wrapped: storage throws outright in some privacy modes rather than merely
 * returning nothing, and a menu preference is not worth a blank page.
 */
const KEY = 'nav.rail';

export function readRailPreference(): boolean {
  try {
    return localStorage.getItem(KEY) === '1';
  } catch {
    return false;
  }
}

export function writeRailPreference(collapsed: boolean): void {
  try {
    localStorage.setItem(KEY, collapsed ? '1' : '0');
  } catch {
    /* not important */
  }
}
