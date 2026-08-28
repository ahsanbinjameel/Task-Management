// Mirrors the API DTOs. Enums travel as **names**, not ordinals — the API registers
// JsonStringEnumConverter — so these are string unions rather than numeric enums. That means a
// reordered enum on the server can never silently change meaning here.

export type WorkTaskStatus =
  | 'Requested' | 'AwaitingReview' | 'ClarificationRequired' | 'Approved'
  | 'ReadyForAssignment' | 'Assigned' | 'ReadyToStart'
  | 'InProgress' | 'Paused' | 'Blocked'
  | 'CompletedReadyForQC' | 'QCReview' | 'QCFailedRework' | 'QCPassed'
  | 'ReadyForClosure' | 'Closed'
  | 'Cancelled' | 'Deferred' | 'OnHold' | 'Duplicate' | 'Reopened';

export type RequestStatus =
  | 'Submitted' | 'InReview' | 'ClarificationRequired' | 'Approved'
  | 'Rejected' | 'Duplicate' | 'Deferred' | 'Escalated'
  /** Routed to a checker to establish whether there is really a problem. No task exists. */
  | 'UnderVerification';

export type Priority = 'Critical' | 'High' | 'Normal' | 'Low';
export type RequestedUrgency = 'Critical' | 'High' | 'Normal' | 'Low';
export type RequestType =
  | 'Bug' | 'ChangeRequest' | 'NewFeature' | 'Support' | 'Configuration' | 'Database'
  | 'Report' | 'Investigation' | 'DataCorrection' | 'Infrastructure' | 'Other';

export type WorkforceState =
  | 'NotLoggedIn' | 'LoggedInShiftNotStarted' | 'Available' | 'Working'
  | 'Break' | 'Lunch' | 'Meeting' | 'TemporarilyAway' | 'ShiftEnded';

export type WorkSessionStatus = 'Active' | 'Paused' | 'Completed' | 'Interrupted';
export type QCResult = 'Passed' | 'Failed' | 'ClarificationRequired';

export type CommentCategory =
  | 'General' | 'RequesterCommunication' | 'Clarification' | 'InternalNote'
  | 'TechnicalNote' | 'ProgressUpdate' | 'QCNote' | 'ResolutionNote' | 'ManagementNote';

export type DependencyType = 'Blocks' | 'DependsOn' | 'Related' | 'Duplicate' | 'ParentChild';

export type TriageOutcome =
  | 'Approve' | 'Reject' | 'RequestClarification' | 'MarkDuplicate' | 'Defer' | 'Escalate'
  /** Have it looked at before deciding. Creates a verification, never a task. */
  | 'SendForVerification';

// --- verification ----------------------------------------------------------------------------
//
// Assigned investigation: "is there actually a problem here?". Deliberately not QC, which asks
// whether finished work meets its acceptance criteria and belongs to a task's lifecycle.

export type VerificationStatus =
  | 'Requested' | 'Assigned' | 'InProgress' | 'Completed' | 'Cancelled';

export type VerificationResult =
  | 'IssueConfirmed' | 'WorkingCorrectly' | 'ConfigurationOrDataIssue'
  | 'NeedsClarification' | 'Inconclusive';

export type VerificationTargetType = 'Request' | 'Form' | 'Module' | 'Build' | 'Other';

// --- shared ---------------------------------------------------------------------------------

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** A known client. The name is what people type; the id is what filters use. */
export interface ClientOptionDto {
  id: number;
  name: string;
}

/** A module, for the verification target picker. Maintained by an administrator, not typed in. */
export interface ModuleOptionDto {
  id: number;
  name: string;
  projectName?: string | null;
}

/** One clickable count above a list. */
/**
 * One tile above a list. `key` names a *view* — a group of internal statuses — not a status:
 * which statuses belong together depends on who is looking, and the server decides that.
 */
export interface StatusCountDto {
  key: string;
  label: string;
  count: number;
}

/** The API's ProblemDetails shape. `code` is the stable identifier — branch on it, never on prose. */
export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
}

// --- identity -------------------------------------------------------------------------------

export interface UserDto {
  id: number;
  userName: string;
  email: string;
  displayName: string;
  isActive: boolean;
  workforceState: string;
  lastLoginAt?: string | null;
  departmentId?: number | null;
  teamId?: number | null;
  roles: string[];
  permissions: string[];
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: UserDto;
}

export interface RoleDto {
  id: number;
  name: string;
  description?: string | null;
  isSystemRole: boolean;
  permissions: string[];
}

// --- requests -------------------------------------------------------------------------------

export interface RequestSummaryDto {
  id: number;
  requestNumber: string;
  title: string;
  type: RequestType;
  status: RequestStatus;
  requestedUrgency: RequestedUrgency;
  requestedByUserId: number;
  requestedByDisplayName: string;
  requestedAt: string;
  targetDate?: string | null;
  generatedTaskId?: number | null;
  attachmentCount: number;
  hasOpenClarification: boolean;
  clientId?: number | null;
  clientName?: string | null;

  // The generated task, folded back onto the request, so the person who asked for the work can
  // see what is happening to it without being sent to a second screen.
  taskStatus?: WorkTaskStatus | null;
  /** The status this reader should be shown — already decided for their audience by the server. */
  viewKey: string;
  viewLabel: string;
  responsibleDisplayName?: string | null;
  progressPercent: number;
  updatedAt?: string | null;
}

/**
 * What happened to a request after approval, in the requester's language — read off the generated
 * task so nobody has to open it. A summary, deliberately not a copy of the task.
 */
export interface RequestProgressDto {
  taskId: number;
  taskNumber: string;
  taskStatus: WorkTaskStatus;
  statusKey: string;
  statusLabel: string;
  responsibleDisplayName?: string | null;
  supportPeople: string[];
  progressPercent: number;
  totalWorkedTime: string;
  startedAt?: string | null;
  dueDate?: string | null;
  latestUpdate?: string | null;
  latestUpdateBy?: string | null;
  latestUpdateAt?: string | null;
  qualityCheck: string;
  waitingReason?: string | null;
}

/** One readable line of what happened to a request. */
export interface RequestActivityDto {
  id: number;
  type: string;
  actorUserId: number;
  actorDisplayName?: string | null;
  occurredAt: string;
  description: string;
}

export interface ClarificationDto {
  id: number;
  question: string;
  askedByUserId: number;
  askedAt: string;
  answer?: string | null;
  answeredByUserId?: number | null;
  answeredAt?: string | null;
}

export interface AttachmentDto {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedByUserId: number;
  uploadedAt: string;
}

/**
 * What a file is *for*, as opposed to what it hangs off.
 *
 * The requester's screenshot of a broken invoice and the worker's screenshot of the fixed one are
 * both "a file on this piece of work"; without this they are the same row in the same list, and
 * "show me the evidence it was actually done" cannot be asked. Mirrors `AttachmentKind` on the
 * server — the API serialises the name, not the ordinal.
 */
export type AttachmentKind =
  | 'General' | 'CompletionProof' | 'QCEvidence'
  /** What a checker attached to an investigation. Belongs to the verification, not to a task. */
  | 'VerificationEvidence';

/**
 * What POST /requests/{id}/triage actually returns — a decision, not the request.
 *
 * This was previously typed as RequestDetailDto. The component assigned it straight into the
 * `request` signal, so every field the template read came back undefined and the page rendered
 * blank; the redirect also silently never fired, because it looked for `generatedTaskId` on an
 * object whose field is `createdTaskId`.
 */
export interface TriageResultDto {
  status: RequestStatus;
  createdTaskId?: number | null;
  createdTaskNumber?: string | null;
  /** Set when the outcome was SendForVerification. Never set alongside a task. */
  verificationId?: number | null;
  verificationNumber?: string | null;
}

export interface RequestDetailDto {
  id: number;
  requestNumber: string;
  title: string;
  description: string;
  type: RequestType;
  status: RequestStatus;
  requestedUrgency: RequestedUrgency;
  clientId?: number | null;
  clientName?: string | null;
  businessImpact?: string | null;
  expectedResult?: string | null;
  currentResult?: string | null;
  reproductionSteps?: string | null;
  requestedByUserId: number;
  requestedByDisplayName: string;
  requestedAt: string;
  targetDate?: string | null;
  /** The product axis: "Sales · Delivery Order · Detail Report". Null until triage places it. */
  productLocation?: string | null;
  relatedRequestId?: number | null;
  /** The number of the request this came out of, when it is a later round (PRODUCT-CORE §6). */
  relatedRequestNumber?: string | null;
  /** Which round of testing found this. 1 for anything raised on its own. */
  round: number;
  generatedTaskId?: number | null;
  activity: RequestActivityDto[];
  clarifications: ClarificationDto[];
  attachments: AttachmentDto[];
  /** Checks raised against this request, newest first. Empty for most requests. */
  verifications: RequestVerificationDto[];
  /** The status this reader should be told — folds in the task where there is one. */
  viewKey: string;
  viewLabel: string;
  /** Null until the request is approved and work exists. */
  progress?: RequestProgressDto | null;
  /** The submission this arrived in, when it was asked for alongside others. */
  batchId?: number | null;
  batchNumber?: string | null;
  /** Which of the batch this was, 1-based. Zero for a request raised on its own. */
  ordinalInBatch: number;
  batchItemCount: number;
}

// --- tasks ----------------------------------------------------------------------------------

export interface TaskSummaryDto {
  id: number;
  taskNumber: string;
  title: string;
  type: RequestType;
  status: WorkTaskStatus;
  priority: Priority;
  primaryAssigneeUserId?: number | null;
  primaryAssigneeDisplayName?: string | null;
  dueDate?: string | null;
  queueOrder: number;
  progressPercent: number;
  estimatedEffortHours?: number | null;
  totalWorkedTime: string;
  hasActiveSession: boolean;
  clientId?: number | null;
  clientName?: string | null;

  // What the contextual grids show. Each view picks the two or three of these that matter to it.
  /** When it entered the status it is in — every "waiting since" column. */
  statusSince?: string | null;
  /** Why, where the move required a reason: the pause or blocking reason. */
  statusReason?: string | null;
  assignedAt?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  requestId?: number | null;
  requestNumber?: string | null;
  requestedByDisplayName?: string | null;
  checkedByDisplayName?: string | null;
  checkedAt?: string | null;
  checkNotes?: string | null;
  qcUserDisplayName?: string | null;
  supportPeople?: string[] | null;

  // What a worker needs before they can start (PRODUCT-CORE §12A).
  /** The product area, beside the client. Together these are the ERP context. */
  moduleId?: number | null;
  moduleName?: string | null;
  /** Module, form and surface joined for reading: "Sales · Delivery Order · Detail Report". */
  productLocation?: string | null;
  /** What "working" is supposed to look like, in the requester's own words. */
  expectedResult?: string | null;
  /** Screenshots and files worth seeing first. Excludes quality-check evidence. */
  attachmentCount: number;
}

/** The state machine's own record: from-and-to, for people who run the process. */
export interface StatusHistoryDto {
  id: number;
  fromStatus: WorkTaskStatus;
  toStatus: WorkTaskStatus;
  changedByUserId: number;
  changedByDisplayName?: string | null;
  changedAt: string;
  reason?: string | null;
  wasOverride: boolean;
}

export interface AssignmentHistoryDto {
  id: number;
  fromUserId?: number | null;
  fromDisplayName?: string | null;
  toUserId?: number | null;
  toDisplayName?: string | null;
  assignedByUserId: number;
  assignedByDisplayName?: string | null;
  assignedAt: string;
  reason?: string | null;
}

/** What happened, in a sentence. The account a person reads. */
export interface TaskActivityDto {
  id: number;
  type: string;
  actorUserId: number;
  actorDisplayName?: string | null;
  occurredAt: string;
  description: string;
}

export interface WorkSessionDto {
  id: number;
  taskId: number;
  userId: number;
  sessionStart: string;
  sessionEnd?: string | null;
  duration?: string | null;
  status: WorkSessionStatus;
  endPauseReasonId?: number | null;
  endPauseReasonName?: string | null;
  endComment?: string | null;
  endedByInterruption: boolean;
  interruptedByTaskId?: number | null;
}

export interface AcceptanceCriterionDto {
  index: number;
  text: string;
  met?: boolean | null;
  note?: string | null;
}

export interface QCReviewDto {
  id: number;
  taskId: number;
  attemptNumber: number;
  reviewerUserId: number;
  reviewerDisplayName?: string | null;
  reviewedAt: string;
  result: QCResult;
  comments?: string | null;
  environment?: string | null;
  buildVersion?: string | null;
  criteria: AcceptanceCriterionDto[];
  /**
   * What the checker attached to *this attempt*. Per attempt, not per task: the pictures that
   * justified a failure stay with the failure once a later attempt passes.
   */
  attachments?: AttachmentDto[] | null;
}

/** A smaller task belonging to a parent, summarised for the parent's own page. */
export interface SubtaskSummaryDto {
  taskId: number;
  taskNumber: string;
  title: string;
  status: WorkTaskStatus;
  responsiblePersonName?: string | null;
  progressPercent: number;
  /** When true the parent cannot be finished until this one is done. */
  isRequired: boolean;
}

/**
 * Someone helping with a task who does not own it. Kept a distinct shape from the assignee so the
 * two cannot be used interchangeably by accident.
 */
export interface SupportPersonDto {
  userId: number;
  displayName: string;
  addedAt: string;
  addedByUserId: number;
}

/**
 * What was originally asked for, carried onto the task. Request and task stay separate records —
 * this is so a worker never has to go and read the request to find the screenshot or what
 * "working" was supposed to look like.
 */
export interface RequestContextDto {
  requestId: number;
  requestNumber: string;
  requestedByDisplayName: string;
  requestedAt: string;
  requestedUrgency: RequestedUrgency;
  projectName?: string | null;
  moduleName?: string | null;
  originalDescription: string;
  businessImpact?: string | null;
  expectedResult?: string | null;
  currentResult?: string | null;
  reproductionSteps?: string | null;
  attachments: AttachmentDto[];
  /** The submission this arrived in, when it arrived alongside others. */
  batchId?: number | null;
  batchNumber?: string | null;
  /**
   * Other requests a reviewer folded into this same task. A worker handed three folded items has
   * to see all three, or "done" gets declared when only the first one is.
   */
  foldedWith?: FoldedRequestDto[] | null;
}

export interface FoldedRequestDto {
  requestId: number;
  requestNumber: string;
  title: string;
  description: string;
  requestedByDisplayName: string;
}

// --- request batches ---------------------------------------------------------------------------

/**
 * Several things asked for at once. A wrapper, not a second workflow: every item is an ordinary
 * request with its own number and its own triage decision, which is why the batch carries no
 * status of its own — only counts a screen can compute.
 */
export interface BatchItemDto {
  title: string;
  description: string;
  type: RequestType;
  requestedUrgency: RequestedUrgency;
  targetDate?: string | null;
}

export interface CreateRequestBatchDto {
  /** Optional — the server names the submission from its first point when this is absent. */
  title?: string | null;
  note?: string | null;
  clientName?: string | null;
  /** Shared product location, copied onto each item. Never a client (PRODUCT-CORE §5). */
  moduleId?: number;
  formId?: number;
  items: BatchItemDto[];
}

export interface ApproveTogetherDto {
  requestIds: number[];
  taskTitle?: string | null;
  approvedPriority?: Priority | null;
  estimatedEffortHours?: number | null;
  dueDate?: string | null;
  acceptanceCriteria?: string | null;
}

export interface BatchItemSummaryDto {
  id: number;
  requestNumber: string;
  ordinal: number;
  title: string;
  type: RequestType;
  requestedUrgency: RequestedUrgency;
  status: RequestStatus;
  /** Plain-language status, from the same map the rest of the app uses. */
  statusLabel: string;
  generatedTaskId?: number | null;
  generatedTaskNumber?: string | null;
  /** Other items of this batch folded into the same task. */
  sharedTaskWith: string[];
}

export interface RequestBatchSummaryDto {
  id: number;
  batchNumber: string;
  title: string;
  requestedByDisplayName: string;
  requestedAt: string;
  clientName?: string | null;
  itemCount: number;
  awaitingDecisionCount: number;
  approvedCount: number;
  declinedCount: number;
}

export interface RequestBatchDetailDto {
  id: number;
  batchNumber: string;
  title: string;
  note?: string | null;
  requestedByUserId: number;
  requestedByDisplayName: string;
  requestedAt: string;
  clientId?: number | null;
  clientName?: string | null;
  items: BatchItemSummaryDto[];
  attachments: AttachmentDto[];
}

export interface TaskDetailDto {
  id: number;
  taskNumber: string;
  requestId?: number | null;
  requestNumber?: string | null;
  title: string;
  description: string;
  type: RequestType;
  status: WorkTaskStatus;
  priority: Priority;
  clientId?: number | null;
  clientName?: string | null;
  /** The product axis: "Sales · Delivery Order · Detail Report". Null until triage places it. */
  productLocation?: string | null;
  primaryAssigneeUserId?: number | null;
  primaryAssigneeDisplayName?: string | null;
  reviewerUserId?: number | null;
  qcUserId?: number | null;
  estimatedEffortHours?: number | null;
  dueDate?: string | null;
  acceptanceCriteria?: string | null;
  resolution?: string | null;
  progressPercent: number;
  queueOrder: number;
  parentTaskId?: number | null;
  availableTransitions: WorkTaskStatus[];
  totalWorkedTime: string;
  supportPeople: SupportPersonDto[];
  workSessions: WorkSessionDto[];
  statusHistory: StatusHistoryDto[];
  assignmentHistory: AssignmentHistoryDto[];
  activity: TaskActivityDto[];
  qcReviews: QCReviewDto[];
  subTasks: SubtaskSummaryDto[];
  /** Task numbers of unfinished work this task waits on. Non-empty blocks the timer. */
  blockedBy: string[];
  rowVersion?: string | null;
  /** Where this work came from. Null for a task raised without a request. */
  request?: RequestContextDto | null;
  /**
   * What the responsible person attached as proof the work is done — kept apart from the
   * request's own screenshots, which describe the problem rather than the fix.
   */
  completionProof?: AttachmentDto[] | null;
  /** Files added to the task for context, rather than as proof of anything. */
  attachments?: AttachmentDto[] | null;
}

export type PauseCategory =
  | 'OtherWorkUrgent' | 'WaitingForSomeone' | 'WaitingForClient' | 'CannotContinue'
  | 'Meeting' | 'Break' | 'Lunch' | 'EndOfShift' | 'Other';

export interface PauseReasonDto {
  id: number;
  name: string;
  requiresComment: boolean;
  /** The task itself cannot move on — not merely that the worker stepped away. */
  isBlocker: boolean;
  category: PauseCategory;
  /** Where the worker goes, if anywhere. Null means they stay on shift and free. */
  awayState?: WorkforceState | null;
}

export interface AssignableUserDto {
  id: number;
  userName: string;
  displayName: string;
  workforceState: WorkforceState;
}

/**
 * One person a task could go to, in facts (PRODUCT-CORE §12C).
 *
 * Note the absence of a capacity number. Summed estimates are guesses added together, and the
 * coordinator was being asked to trust a figure nobody could act on.
 */
export interface AssignmentCandidateDto {
  userId: number;
  displayName: string;
  workforceState: WorkforceState;
  /** On the clock right now. */
  isOnShift: boolean;

  activeTaskId?: number | null;
  activeTaskNumber?: string | null;
  activeTaskTitle?: string | null;
  /** How long the running timer has been going. */
  activeFor?: string | null;

  activeCount: number;
  waitingCount: number;
  dueTodayCount: number;

  /** Work they have recently done on the same client or module. */
  recentRelated: string[];
}

export interface WorkloadDto {
  userId: number;
  displayName: string;
  workforceState: WorkforceState;
  openTaskCount: number;
  inProgressCount: number;
  blockedCount: number;
  estimatedHoursOutstanding: number;
  activeTaskId?: number | null;
  activeTaskNumber?: string | null;
  /** On the clock right now. Decided by the server's state machine, not re-listed here. */
  isOnShift: boolean;
}

// --- collaboration --------------------------------------------------------------------------

export interface TaskCommentDto {
  id: number;
  taskId: number;
  authorUserId: number;
  authorDisplayName?: string | null;
  category: CommentCategory;
  body: string;
  visibleToRequester: boolean;
  createdAt: string;
}

export interface TaskDependencyDto {
  id: number;
  taskId: number;
  relatedTaskId: number;
  relatedTaskNumber: string;
  relatedTaskTitle: string;
  relatedTaskStatus: WorkTaskStatus;
  type: DependencyType;
  isBlocking: boolean;
}

export interface TaskDependencyGraphDto {
  taskId: number;
  outgoing: TaskDependencyDto[];
  incoming: TaskDependencyDto[];
  isBlocked: boolean;
  blockedBy: string[];
}

export interface ScopeChangeDto {
  id: number;
  taskId: number;
  requestedByUserId: number;
  requestedByDisplayName?: string | null;
  requestedAt: string;
  description: string;
  reason?: string | null;
  estimatedImpactHours?: number | null;
  deadlineImpact?: string | null;
  approvedByUserId?: number | null;
  approvedAt?: string | null;
}

export interface ClosureRequirementDto {
  code: string;
  description: string;
  isMet: boolean;
  detail?: string | null;
}

export interface ClosureChecklistDto {
  taskId: number;
  isReady: boolean;
  requirements: ClosureRequirementDto[];

  // The acceptance policy (PRODUCT-CORE §7). Reported beside the requirements, not as one of
  // them: a coordinator whose requester has gone quiet is told, not blocked.
  /** Whether someone asked for this work and can therefore confirm the fix. */
  requiresRequesterAcceptance: boolean;
  requesterDisplayName?: string | null;
  /** True once it is closed — acceptance and closure are the same act. */
  requesterHasConfirmed: boolean;
}

export interface AcceptanceCriteriaDto {
  taskId: number;
  criteria: AcceptanceCriterionDto[];
  evaluatedInAttempt?: number | null;
  evaluatedAt?: string | null;
}

// --- workforce ------------------------------------------------------------------------------

export interface ShiftSessionDto {
  id: number;
  userId: number;
  userDisplayName: string;
  shiftStart: string;
  shiftEnd?: string | null;
  duration?: string | null;
  endedImproperly: boolean;
  endedByUserId?: number | null;
  endNote?: string | null;
}

export interface WorkforceStatusDto {
  userId: number;
  userDisplayName: string;
  state: WorkforceState;
  stateLabel: string;
  isOnShift: boolean;
  /** False for people not on the clock — hide the shift controls rather than offer a 403. */
  isShiftTracked: boolean;
  stateSince?: string | null;
  currentShift?: ShiftSessionDto | null;
  availableStates: WorkforceState[];
}

export interface ActivityEventDto {
  id: number;
  occurredAt: string;
  label: string;
  resultingState?: WorkforceState | null;
  relatedTaskId?: number | null;
  note?: string | null;
}

export interface TimelineEntryDto {
  from: string;
  to: string;
  duration: string;
  label: string;
  state?: WorkforceState | null;
  relatedTaskId?: number | null;
  note?: string | null;
  isOpen: boolean;
}

export interface DailyTimelineDto {
  userId: number;
  userDisplayName: string;
  date: string;
  entries: TimelineEntryDto[];
  totalOnShift: string;
  totalProductive: string;
  totalAway: string;
  timeByState: Record<string, string>;
}

export interface ActiveWorkerDto {
  userId: number;
  userName: string;
  displayName: string;
  departmentId?: number | null;
  teamId?: number | null;
  state: WorkforceState;
  shiftStart: string;
  activeTaskId?: number | null;
  activeTaskNumber?: string | null;
}

export interface ActiveWorkforceDto {
  asOf: string;
  totalOnShift: number;
  working: number;
  available: number;
  away: number;
  workers: ActiveWorkerDto[];
}

// --- dashboards & reports ---------------------------------------------------------------------

export interface DashboardItemDto {
  id: number;
  number: string;
  title: string;
  status: string;
  priority: Priority;
  dueDate?: string | null;
  isOverdue: boolean;
}

/** What a home-screen row points at, so the client knows which route to build. */
export type AttentionSubject = 'Task' | 'Request';

/**
 * One thing waiting on the signed-in user. `reason` is the point of the row — it is written for a
 * person by the server, so the wording cannot drift between the two halves of the app.
 */
export interface AttentionItemDto {
  subject: AttentionSubject;
  id: number;
  number: string;
  title: string;
  reason: string;
  rank: number;
  priority: Priority;
  /** When it entered the state that put it here. The basis for "waiting 3 days". */
  since: string;
  dueDate?: string | null;
  isOverdue: boolean;
}

/** Something that happened. Past tense, nothing to act on. */
export interface ActivityItemDto {
  subject: AttentionSubject;
  id: number;
  number: string;
  text: string;
  at: string;
}

export interface HomeDashboardDto {
  needsAttention: AttentionItemDto[];
  recentActivity: ActivityItemDto[];
  /** The count before truncation, so the page can offer "and N more". */
  totalNeedingAttention: number;
}

export interface RequesterDashboardDto {
  submittedCount: number;
  underReviewCount: number;
  awaitingMyClarificationCount: number;
  inProgressCount: number;
  closedCount: number;
  rejectedCount: number;
  recent: DashboardItemDto[];
}

export interface WorkerDashboardDto {
  queueLength: number;
  inProgressCount: number;
  blockedCount: number;
  reworkCount: number;
  overdueCount: number;
  activeTaskId?: number | null;
  activeTaskNumber?: string | null;
  isOnShift: boolean;
  workedToday: string;
  unreadNotifications: number;
  queue: DashboardItemDto[];
}

export interface CoordinatorDashboardDto {
  awaitingReviewCount: number;
  unassignedCount: number;
  blockedCount: number;
  awaitingQCCount: number;
  overdueCount: number;
  peopleOnShift: number;
  peopleWorking: number;
  unassigned: DashboardItemDto[];
  overdue: DashboardItemDto[];
}

export interface CountByLabelDto {
  label: string;
  count: number;
}

export interface ManagementDashboardDto {
  from: string;
  to: string;
  requestsRaised: number;
  tasksCreated: number;
  tasksClosed: number;
  qcAttempts: number;
  qcFailures: number;
  qcPassRate: number;
  averageCycleTimeHours?: number | null;
  totalHoursWorked: number;
  openTaskCount: number;
  overdueCount: number;
  openByStatus: CountByLabelDto[];
  openByPriority: CountByLabelDto[];
  closedByAssignee: CountByLabelDto[];
}

export interface SupportedTaskDto {
  taskId: number;
  taskNumber: string;
  title: string;
  status: string;
  responsiblePersonName?: string | null;
}

export interface TaskTimeDto {
  taskId: number;
  taskNumber: string;
  title: string;
  timeSpent: string;
  sessions: number;
}

export interface DailyUserReportDto {
  date: string;
  userId: number;
  displayName: string;
  shiftStart?: string | null;
  shiftEnd?: string | null;
  shiftDuration: string;
  productiveTime: string;
  breakTime: string;
  tasksWorked: number;
  tasksCompleted: number;
  /** Time on tasks this person is responsible for. */
  ownedWork: TaskTimeDto[];
  /** Time on other people's tasks. Never added to the owned figures. */
  supportWork: TaskTimeDto[];
  /** Tasks they are helping with, whether or not they logged time today. */
  supportingOn: SupportedTaskDto[];
  /** Work that never came through the front door. Its own line: it is neither owned nor support. */
  quickWork: QuickWorkLineDto[];
  /** Time on finished quick work. Cancelled records are shown but not counted. */
  quickWorkTime: string;
  /** How many times a running task was put down for something else. */
  interruptions: number;
}

export interface QuickWorkLineDto {
  id: number;
  title: string;
  startedAt: string;
  duration: string;
  clientName?: string | null;
  outcome?: string | null;
  interruptedTaskNumber?: string | null;
  promotedToRequestNumber?: string | null;
  wasCancelled: boolean;
}

export interface DailyTeamReportDto {
  date: string;
  peopleOnShift: number;
  totalShiftTime: string;
  totalProductiveTime: string;
  tasksCompleted: number;
  users: DailyUserReportDto[];
}

// --- quick work -----------------------------------------------------------------------------

export type QuickWorkStatus = 'Active' | 'Finished' | 'Cancelled';

/**
 * The five-minute job that arrived by phone. Not a task: no lifecycle, no assignee, no quality
 * check. A title, a clock and an outcome.
 */
export interface QuickWorkDto {
  id: number;
  title: string;
  userId: number;
  userDisplayName?: string | null;
  startedAt: string;
  endedAt?: string | null;
  /** Climbs while it is running, so the screen needs no arithmetic of its own. */
  duration: string;
  status: QuickWorkStatus;
  clientId?: number | null;
  clientName?: string | null;
  outcome?: string | null;
  /** The task it displaced, so the screen can offer to hand the work back. */
  interruptedTaskId?: number | null;
  interruptedTaskNumber?: string | null;
  promotedToRequestId?: number | null;
  promotedToRequestNumber?: string | null;
}

export interface StartQuickWorkDto {
  title: string;
  clientName?: string | null;
  pauseReasonId?: number | null;
}

export interface FinishQuickWorkDto {
  outcome: string;
  resumeInterruptedTask: boolean;
}

export interface PromoteQuickWorkDto {
  title?: string | null;
  description: string;
  type: RequestType;
  requestedUrgency: RequestedUrgency;
}

// --- notifications & audit ----------------------------------------------------------------------

export interface NotificationDto {
  id: number;
  title: string;
  body?: string | null;
  linkEntityType?: string | null;
  linkEntityId?: number | null;
  isRead: boolean;
  createdAt: string;
  readAt?: string | null;
}

export interface AuditLogDto {
  id: number;
  createdAt: string;
  actorUserId?: number | null;
  actorDisplayName?: string | null;
  action: string;
  entityType?: string | null;
  entityId?: number | null;
  previousValues?: string | null;
  newValues?: string | null;
  ipAddress?: string | null;
  deviceInfo?: string | null;
}

// --- administrator setup data ------------------------------------------------------------------
//
// The reference lists an administrator maintains. Each one carries a usage count, because the only
// question worth asking before changing a lookup is "what already points at this?" — and each is
// deactivated rather than deleted, so the count keeps answering for the history.

export interface SetupClientDto {
  id: number;
  name: string;
  code?: string | null;
  isActive: boolean;
  requestCount: number;
}

export interface SetupDepartmentDto {
  id: number;
  name: string;
  isActive: boolean;
  teamCount: number;
}

export interface SetupTeamDto {
  id: number;
  name: string;
  departmentId?: number | null;
  departmentName?: string | null;
  isActive: boolean;
}

/** The editable form of PauseReasonDto — same row, plus what it costs to change it. */
export interface SetupPauseReasonDto {
  id: number;
  name: string;
  requiresComment: boolean;
  isBlocker: boolean;
  category: PauseCategory;
  awayState?: WorkforceState | null;
  isActive: boolean;
  timesUsed: number;
}

export interface RoleDetailDto {
  id: number;
  name: string;
  description?: string | null;
  isSystemRole: boolean;
  userCount: number;
  permissions: string[];
}

/**
 * Which values each column's filter can still offer, given what the other columns are filtered by
 * — the "like Excel" behaviour. Raw tokens (enum names, ids as strings); the client keeps its own
 * labelled option list and hides whatever this does not mention.
 */
export interface FilterOptionsDto {
  columns: Record<string, string[]>;
}


// --- verification DTOs -------------------------------------------------------------------------

/** A check as it appears on the request that spawned it. A summary; the full page is one click on. */
export interface RequestVerificationDto {
  id: number;
  verificationNumber: string;
  status: VerificationStatus;
  /** Server-owned wording, so the two sides cannot drift. */
  statusLabel: string;
  assignedToUserId?: number | null;
  assignedToDisplayName?: string | null;
  requestedAt: string;
  completedAt?: string | null;
  result?: VerificationResult | null;
  resultLabel?: string | null;
  findings?: string | null;
}

export interface VerificationSummaryDto {
  id: number;
  verificationNumber: string;
  title: string;
  status: VerificationStatus;
  statusLabel: string;
  priority: Priority;
  targetType: VerificationTargetType;
  /** The target in one line, whichever kind it is. */
  targetSummary: string;
  requestedByUserId: number;
  requestedByDisplayName: string;
  requestedAt: string;
  assignedToUserId?: number | null;
  assignedToDisplayName?: string | null;
  result?: VerificationResult | null;
  resultLabel?: string | null;
  completedAt?: string | null;
  requestId?: number | null;
  requestNumber?: string | null;
  attachmentCount: number;
}

export interface VerificationActivityDto {
  id: number;
  type: string;
  actorUserId: number;
  actorDisplayName?: string | null;
  occurredAt: string;
  description: string;
}

export interface VerificationDetailDto {
  id: number;
  verificationNumber: string;
  title: string;
  instructions?: string | null;
  expectedBehavior?: string | null;
  status: VerificationStatus;
  statusLabel: string;
  priority: Priority;
  targetType: VerificationTargetType;
  targetSummary: string;
  moduleId?: number | null;
  moduleName?: string | null;
  targetName?: string | null;
  targetReference?: string | null;
  requestedByUserId: number;
  requestedByDisplayName: string;
  requestedAt: string;
  assignedToUserId?: number | null;
  assignedToDisplayName?: string | null;
  assignedByUserId?: number | null;
  assignedAt?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  result?: VerificationResult | null;
  resultLabel?: string | null;
  findings?: string | null;
  cancellationReason?: string | null;
  requestId?: number | null;
  requestNumber?: string | null;
  requestTitle?: string | null;
  activity: VerificationActivityDto[];
  attachments: AttachmentDto[];
  rowVersion?: string | null;
}

/** Somebody a check can be given to, with how much they are already holding. */
export interface AssignableCheckerDto {
  userId: number;
  displayName: string;
  openVerifications: number;
}

export interface CreateVerificationDto {
  title: string;
  instructions?: string | null;
  expectedBehavior?: string | null;
  targetType: VerificationTargetType;
  moduleId?: number | null;
  targetName?: string | null;
  targetReference?: string | null;
  priority: Priority;
  assignToUserId?: number | null;
}

/** Routing a request to a checker instead of deciding it. Carried inside the triage payload. */
export interface SendForVerificationDto {
  title?: string | null;
  instructions?: string | null;
  expectedBehavior?: string | null;
  targetType: VerificationTargetType;
  moduleId?: number | null;
  targetName?: string | null;
  targetReference?: string | null;
  priority?: Priority | null;
  assignToUserId?: number | null;
}

export interface AssignVerificationDto {
  assignToUserId: number;
  /** Mandatory when taking it off somebody who already had it. */
  reason?: string | null;
}

export interface RecordVerificationResultDto {
  result: VerificationResult;
  findings: string;
}

export interface CancelVerificationDto {
  reason: string;
}

// --- the product catalog (PRODUCT-CORE §5) --------------------------------------------------------
//
// Module → Form → Surface, and nothing in it references a client. Your product has these; a client
// runs an instance of it. Per-client copies would make "which forms generate the most support"
// unanswerable.

export interface ModuleDto {
  id: number;
  name: string;
  isActive: boolean;
  /** How many forms hang off it — the retire warning. */
  forms: number;
  usedBy: number;
}

export interface FormDto {
  id: number;
  name: string;
  moduleId: number;
  moduleName: string;
  isActive: boolean;
  surfaces: number;
  usedBy: number;
}

export interface FormSurfaceDto {
  id: number;
  name: string;
  formId: number;
  formName: string;
  moduleName: string;
  isActive: boolean;
  usedBy: number;
}

/** A form for the picker. The module comes with it, because "Adjustment" alone is ambiguous. */
export interface FormOptionDto {
  id: number;
  name: string;
  moduleId: number;
  moduleName: string;
}

export interface FormSurfaceOptionDto {
  id: number;
  name: string;
  formId: number;
  formName: string;
}
