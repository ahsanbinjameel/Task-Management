import { TaskSummaryDto } from '../core/models';

/**
 * What each status view puts in the grid.
 *
 * The server decides which internal statuses a view covers; this decides what is worth *showing*
 * once you are looking at it. They are separate on purpose — one is a rule about the workflow, the
 * other is a judgement about a screen.
 *
 * The point is that a fixed column set is wrong nearly everywhere. "Worked time" on a queue nobody
 * has started is a column of dashes; "due date" on finished work is noise; and the one thing a
 * coordinator wants to know about the unassigned pile — how long it has been sitting there — is
 * not on the default grid at all. So each view names its own columns and its own primary action,
 * and the table renders whatever it is given.
 */
export interface ListView {
  /** Columns, in order. Names must exist in `app-task-table`. */
  columns: string[];
  /** The one action on the row. Anything rarer belongs behind the row's menu. */
  action?: { label: string; /** Only offered when the row allows it. */ when?: (t: TaskSummaryDto) => boolean };
}

const DEFAULT_VIEW: ListView = {
  columns: ['number', 'title', 'client', 'status', 'priority', 'assignee', 'due', 'worked'],
};

/** A worker can only start work that is theirs and not already running. */
const startable = (t: TaskSummaryDto) => !t.hasActiveSession;

/**
 * Keyed by the view key the server sends. Two audiences share a key where they mean the same
 * thing ("working"), and the columns follow the key rather than the audience — a coordinator and
 * a worker looking at running work both want to know when it started and how long it has taken.
 */
const TASK_VIEWS: Record<string, ListView> = {
  // Nobody has this yet, so there is no assignee, no worked time and no progress to show. What
  // matters is how long it has been waiting and who is waiting on it.
  unassigned: {
    columns: ['number', 'title', 'client', 'priority', 'waitingSince', 'requestedBy', 'estimate'],
    action: { label: 'Assign' },
  },

  todo: {
    columns: ['number', 'title', 'client', 'assignee', 'priority', 'assignedAt', 'due'],
    action: { label: 'Start work', when: startable },
  },
  assigned: {
    columns: ['number', 'title', 'client', 'assignee', 'priority', 'assignedAt', 'due'],
    action: { label: 'Open' },
  },

  working: {
    columns: ['number', 'title', 'client', 'assignee', 'startedAt', 'worked', 'due', 'progress'],
  },

  // Paused and blocked share a grid: the question is the same either way — how long, and why.
  waiting: {
    columns: ['number', 'title', 'assignee', 'waitingSince', 'reason', 'worked'],
    action: { label: 'Open' },
  },
  paused: {
    columns: ['number', 'title', 'assignee', 'waitingSince', 'reason', 'worked'],
    action: { label: 'Open' },
  },
  blocked: {
    columns: ['number', 'title', 'assignee', 'waitingSince', 'reason', 'worked'],
    action: { label: 'Open' },
  },

  checking: {
    columns: ['number', 'title', 'assignee', 'completedAt', 'checker', 'waitingSince'],
    action: { label: 'Check' },
  },

  fixing: {
    columns: ['number', 'title', 'assignee', 'checkedBy', 'checkedAt', 'checkNotes'],
    action: { label: 'Start fixing', when: startable },
  },

  passed: {
    columns: ['number', 'title', 'client', 'assignee', 'checkedBy', 'checkedAt', 'worked'],
    action: { label: 'Open' },
  },

  done: {
    columns: ['number', 'title', 'client', 'assignee', 'checkedBy', 'completedAt', 'worked'],
  },
  closed: {
    columns: ['number', 'title', 'client', 'assignee', 'checkedBy', 'completedAt', 'worked'],
  },

  stopped: {
    columns: ['number', 'title', 'client', 'status', 'assignee', 'statusSince', 'reason'],
  },
  declined: {
    columns: ['number', 'title', 'client', 'status', 'statusSince', 'reason'],
  },
  approved: {
    columns: ['number', 'title', 'client', 'priority', 'waitingSince', 'requestedBy', 'estimate'],
  },
};

/** The columns and action for a view, falling back to the general-purpose grid. */
export function taskView(key: string | null): ListView {
  return (key && TASK_VIEWS[key]) || DEFAULT_VIEW;
}
