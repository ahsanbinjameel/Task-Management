/**
 * The words users see, in one place.
 *
 * Enum names are the schema — `CompletedReadyForQC`, `RequesterCommunication`, `DependsOn`. They
 * are for the code and the database, and putting them in front of someone who does not work on
 * the system is showing them our tables. `humanizeEnum` split the PascalCase and stopped there,
 * which produced "Completed Ready For QC" and "Q C Failed Rework" — readable characters, not
 * readable English.
 *
 * This is the client half of the wording layer. The server half is
 * `WorkflowApp.Application.Common.StatusLabels`, and the status maps below are kept **identical**
 * to it on purpose: the same state must not be called two different things depending on whether
 * the sentence came from an API message or from a template. When you change a status name, change
 * it in both files.
 *
 * Everything else here — roles, actions, categories, dependency types, pause categories — is
 * client-only, because the server never names them in prose.
 */

import {
  CommentCategory,
  DependencyType,
  PauseCategory,
  Priority,
  QCResult,
  RequestStatus,
  RequestType,
  RequestedUrgency,
  TriageOutcome,
  WorkSessionStatus,
  WorkTaskStatus,
  WorkforceState,
} from './models';

/** Mirrors `StatusLabels.TaskLabels`. */
const TASK_STATUS: Record<WorkTaskStatus, string> = {
  Requested: 'Requested',
  AwaitingReview: 'Waiting for review',
  ClarificationRequired: 'Needs information',
  Approved: 'Approved',
  ReadyForAssignment: 'Waiting to be given out',
  Assigned: 'Assigned',
  ReadyToStart: 'Ready to start',
  InProgress: 'In progress',
  Paused: 'Paused',
  Blocked: 'Cannot continue',
  CompletedReadyForQC: 'Waiting for quality check',
  QCReview: 'Being checked',
  QCFailedRework: 'Needs fixing',
  QCPassed: 'Passed the check',
  ReadyForClosure: 'Ready to close',
  Closed: 'Closed',
  Cancelled: 'Cancelled',
  Deferred: 'Postponed',
  OnHold: 'On hold',
  Duplicate: 'Duplicate',
  Reopened: 'Opened again',
};

/** Mirrors `StatusLabels.RequestLabels`. */
const REQUEST_STATUS: Record<RequestStatus, string> = {
  Submitted: 'Waiting for review',
  InReview: 'Being reviewed',
  ClarificationRequired: 'Needs information',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Duplicate: 'Duplicate',
  Deferred: 'Postponed',
  Escalated: 'Escalated',
};

const WORKFORCE_STATE: Record<WorkforceState, string> = {
  NotLoggedIn: 'Not signed in',
  LoggedInShiftNotStarted: 'Not started yet',
  Available: 'Free',
  Working: 'Working',
  Break: 'On a break',
  Lunch: 'At lunch',
  Meeting: 'In a meeting',
  TemporarilyAway: 'Away from desk',
  ShiftEnded: 'Finished for the day',
};

const REQUEST_TYPE: Record<RequestType, string> = {
  Bug: 'Something is broken',
  ChangeRequest: 'Change to something existing',
  NewFeature: 'Something new',
  Support: 'Help or a question',
  Configuration: 'Settings change',
  Database: 'Database work',
  Report: 'A report',
  Investigation: 'Look into something',
  DataCorrection: 'Fix wrong data',
  Infrastructure: 'Servers or infrastructure',
  Other: 'Something else',
};

const PRIORITY: Record<Priority, string> = {
  Critical: 'Critical',
  High: 'High',
  Normal: 'Normal',
  Low: 'Low',
};

/**
 * Deliberately the same four words as priority. Urgency is what the requester asked for and
 * priority is what was agreed, so the two must be comparable at a glance — the same value reading
 * "Needed immediately" in one column and "Critical" in the next is a difference that is not there.
 * The request form explains the distinction in its label instead.
 */
const URGENCY: Record<RequestedUrgency, string> = {
  Critical: 'Critical',
  High: 'High',
  Normal: 'Normal',
  Low: 'Low',
};

const COMMENT_CATEGORY: Record<CommentCategory, string> = {
  General: 'Note',
  RequesterCommunication: 'Message to the requester',
  Clarification: 'Question',
  InternalNote: 'Internal note',
  TechnicalNote: 'Technical note',
  ProgressUpdate: 'Progress update',
  QCNote: 'Quality check note',
  ResolutionNote: 'How it was resolved',
  ManagementNote: 'Management note',
};

const DEPENDENCY_TYPE: Record<DependencyType, string> = {
  DependsOn: 'Waits for',
  Blocks: 'Holds up',
  Related: 'Related to',
  Duplicate: 'Same as',
  ParentChild: 'Part of',
};

const PAUSE_CATEGORY: Record<PauseCategory, string> = {
  OtherWorkUrgent: 'Something more urgent came up',
  WaitingForSomeone: 'Waiting for someone',
  WaitingForClient: 'Waiting for the client',
  CannotContinue: 'Cannot continue',
  Meeting: 'In a meeting',
  Break: 'On a break',
  Lunch: 'At lunch',
  EndOfShift: 'End of the day',
  Other: 'Something else',
};

const QC_RESULT: Record<QCResult, string> = {
  Passed: 'Passed',
  Failed: 'Needs fixing',
  ClarificationRequired: 'Question raised',
};

const SESSION_STATUS: Record<WorkSessionStatus, string> = {
  Active: 'Running',
  Paused: 'Paused',
  Completed: 'Finished',
  Interrupted: 'Interrupted',
};

const TRIAGE_OUTCOME: Record<TriageOutcome, string> = {
  Approve: 'Approve and create work',
  Reject: 'Reject',
  RequestClarification: 'Ask for more information',
  MarkDuplicate: 'Mark as a duplicate',
  Defer: 'Postpone',
  Escalate: 'Escalate',
};

/**
 * Role names as people say them. The seeded role names are already close, but `AssignmentManager`
 * and `QC` are not words anyone says out loud, and the API returns the internal name.
 */
const ROLE: Record<string, string> = {
  Administrator: 'Administrator',
  Requester: 'Requester',
  Reviewer: 'Reviewer',
  AssignmentManager: 'Coordinator',
  Worker: 'Worker',
  QC: 'Quality checker',
  Management: 'Management',
};

/**
 * Last resort for a value that reached the UI without a translation — a new enum member, or a
 * lookup name from the database. Splits PascalCase but keeps acronym runs together, so `QCReview`
 * reads "QC Review" rather than "Q C Review".
 */
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

function lookup<T extends string>(map: Record<string, string>, value: T | null | undefined): string {
  if (!value) return '';
  return map[value] ?? humanizeEnum(value);
}

export const taskStatusLabel = (v: WorkTaskStatus | null | undefined) => lookup(TASK_STATUS, v);
export const requestStatusLabel = (v: RequestStatus | null | undefined) => lookup(REQUEST_STATUS, v);
export const workforceStateLabel = (v: WorkforceState | null | undefined) => lookup(WORKFORCE_STATE, v);
export const requestTypeLabel = (v: RequestType | null | undefined) => lookup(REQUEST_TYPE, v);
export const priorityLabel = (v: Priority | null | undefined) => lookup(PRIORITY, v);
export const urgencyLabel = (v: RequestedUrgency | null | undefined) => lookup(URGENCY, v);
export const commentCategoryLabel = (v: CommentCategory | null | undefined) => lookup(COMMENT_CATEGORY, v);
export const dependencyTypeLabel = (v: DependencyType | null | undefined) => lookup(DEPENDENCY_TYPE, v);
export const pauseCategoryLabel = (v: PauseCategory | null | undefined) => lookup(PAUSE_CATEGORY, v);
export const qcResultLabel = (v: QCResult | null | undefined) => lookup(QC_RESULT, v);
export const sessionStatusLabel = (v: WorkSessionStatus | null | undefined) => lookup(SESSION_STATUS, v);
export const triageOutcomeLabel = (v: TriageOutcome | null | undefined) => lookup(TRIAGE_OUTCOME, v);
export const roleLabel = (v: string | null | undefined) => lookup(ROLE, v);

/**
 * What the chip components need: one entry point, given the kind of value being rendered. Keeping
 * this switch here rather than in `ChipComponent` means a template that renders a comment category
 * gets the same words as a filter dropdown listing them.
 */
export type LabelKind =
  | 'status' | 'requestStatus' | 'priority' | 'urgency' | 'workforce' | 'requestType'
  | 'commentCategory' | 'dependencyType' | 'pauseCategory' | 'qcResult' | 'sessionStatus'
  | 'triageOutcome' | 'role' | 'plain';

export function label(kind: LabelKind, value: string | null | undefined): string {
  switch (kind) {
    case 'status': return taskStatusLabel(value as WorkTaskStatus);
    case 'requestStatus': return requestStatusLabel(value as RequestStatus);
    case 'priority': return priorityLabel(value as Priority);
    case 'urgency': return urgencyLabel(value as RequestedUrgency);
    case 'workforce': return workforceStateLabel(value as WorkforceState);
    case 'requestType': return requestTypeLabel(value as RequestType);
    case 'commentCategory': return commentCategoryLabel(value as CommentCategory);
    case 'dependencyType': return dependencyTypeLabel(value as DependencyType);
    case 'pauseCategory': return pauseCategoryLabel(value as PauseCategory);
    case 'qcResult': return qcResultLabel(value as QCResult);
    case 'sessionStatus': return sessionStatusLabel(value as WorkSessionStatus);
    case 'triageOutcome': return triageOutcomeLabel(value as TriageOutcome);
    case 'role': return roleLabel(value);
    default: return humanizeEnum(value);
  }
}

/**
 * The verbs on buttons and in confirmations, so "Cannot continue" is not "Block" on one screen and
 * "Blocked" on another. Keyed by the internal action name used in the code.
 */
export const ACTIONS: Record<string, string> = {
  start: 'Start work',
  resume: 'Carry on',
  pause: 'Pause',
  block: 'Cannot continue',
  complete: 'Finished',
  assign: 'Give to someone',
  reassign: 'Give to someone else',
  review: 'Review',
  approve: 'Approve',
  reject: 'Reject',
  qcStart: 'Start checking',
  qcPass: 'Passes',
  qcFail: 'Needs fixing',
  close: 'Close',
  reopen: 'Open again',
  cancel: 'Cancel',
  addSupport: 'Add a support person',
  addSubtask: 'Add a smaller task',
};

export const actionLabel = (key: string) => ACTIONS[key] ?? humanizeEnum(key);
