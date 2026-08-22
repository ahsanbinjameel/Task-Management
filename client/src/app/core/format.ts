import { Pipe, PipeTransform } from '@angular/core';
import { Priority, WorkTaskStatus, WorkforceState } from './models';

/**
 * .NET `TimeSpan` serialises as `[-][d.]hh:mm:ss[.fffffff]`. Rendering that raw in a UI is how you
 * get "02:30:00.0000000" on a dashboard, so everything goes through here.
 */
export function parseTimeSpan(value: string | null | undefined): number {
  if (!value) return 0;

  const match = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value.trim());
  if (!match) return 0;

  const [, sign, days, hours, minutes, seconds, fraction] = match;
  const total =
    (Number(days ?? 0) * 86400 + Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds)) *
      1000 +
    (fraction ? Number(`0.${fraction}`) * 1000 : 0);

  return sign === '-' ? -total : total;
}

/** "3h 25m", "45m", "—". Compact enough for a table cell, unambiguous enough for a report. */
export function humanizeDuration(ms: number): string {
  if (!ms || ms <= 0) return '—';

  const totalMinutes = Math.round(ms / 60000);
  if (totalMinutes < 1) return '<1m';

  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;

  if (hours === 0) return `${minutes}m`;
  if (minutes === 0) return `${hours}h`;
  return `${hours}h ${minutes}m`;
}

@Pipe({ name: 'duration' })
export class DurationPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return humanizeDuration(parseTimeSpan(value));
  }
}

/** Splits PascalCase into words but keeps acronym runs: CompletedReadyForQC → "Completed Ready For QC". */
export function humanizeEnum(value: string | null | undefined): string {
  if (!value) return '';

  let result = '';
  for (let i = 0; i < value.length; i++) {
    const char = value[i];
    if (i > 0 && char >= 'A' && char <= 'Z' && !(value[i - 1] >= 'A' && value[i - 1] <= 'Z')) {
      result += ' ';
    }
    result += char;
  }
  return result;
}

@Pipe({ name: 'humanize' })
export class HumanizePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return humanizeEnum(value);
  }
}

/**
 * Status colour. Grouped by what the status *means* to someone scanning a list — waiting, running,
 * stuck, done — rather than one colour per value, which would just be noise.
 */
export function statusTone(status: WorkTaskStatus): string {
  switch (status) {
    case 'InProgress':
      return 'running';
    case 'Blocked':
    case 'QCFailedRework':
      return 'danger';
    case 'Paused':
    case 'OnHold':
    case 'Deferred':
      return 'warn';
    case 'CompletedReadyForQC':
    case 'QCReview':
      return 'review';
    case 'QCPassed':
    case 'ReadyForClosure':
      return 'good';
    case 'Closed':
      return 'done';
    case 'Cancelled':
    case 'Duplicate':
      return 'muted';
    case 'Reopened':
      return 'danger';
    default:
      return 'neutral';
  }
}

export function priorityTone(priority: Priority): string {
  switch (priority) {
    case 'Critical': return 'danger';
    case 'High': return 'warn';
    case 'Normal': return 'neutral';
    case 'Low': return 'muted';
  }
}

export function workforceTone(state: WorkforceState): string {
  switch (state) {
    case 'Working': return 'running';
    case 'Available': return 'good';
    case 'Break':
    case 'Lunch':
    case 'Meeting':
    case 'TemporarilyAway': return 'warn';
    case 'ShiftEnded':
    case 'NotLoggedIn': return 'muted';
    default: return 'neutral';
  }
}

/** Windows-friendly download of a blob the API returned. */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

/** Today as `yyyy-MM-dd`, which is what the API's DateOnly parameters expect. */
export function isoDate(date: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
