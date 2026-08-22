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
  | 'Rejected' | 'Duplicate' | 'Deferred' | 'Escalated';

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
  | 'Approve' | 'Reject' | 'RequestClarification' | 'MarkDuplicate' | 'Defer' | 'Escalate';

// --- shared ---------------------------------------------------------------------------------

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
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

export interface RequestDetailDto {
  id: number;
  requestNumber: string;
  title: string;
  description: string;
  type: RequestType;
  status: RequestStatus;
  requestedUrgency: RequestedUrgency;
  projectId?: number | null;
  clientId?: number | null;
  moduleId?: number | null;
  businessImpact?: string | null;
  expectedResult?: string | null;
  currentResult?: string | null;
  reproductionSteps?: string | null;
  requestedByUserId: number;
  requestedByDisplayName: string;
  requestedAt: string;
  targetDate?: string | null;
  relatedRequestId?: number | null;
  generatedTaskId?: number | null;
  clarifications: ClarificationDto[];
  attachments: AttachmentDto[];
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
}

export interface StatusHistoryDto {
  id: number;
  fromStatus: WorkTaskStatus;
  toStatus: WorkTaskStatus;
  changedByUserId: number;
  changedAt: string;
  reason?: string | null;
  wasOverride: boolean;
}

export interface AssignmentHistoryDto {
  id: number;
  fromUserId?: number | null;
  toUserId?: number | null;
  assignedByUserId: number;
  assignedAt: string;
  reason?: string | null;
}

export interface TaskActivityDto {
  id: number;
  type: string;
  actorUserId: number;
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
  projectId?: number | null;
  clientId?: number | null;
  moduleId?: number | null;
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
  collaboratorUserIds: number[];
  workSessions: WorkSessionDto[];
  statusHistory: StatusHistoryDto[];
  assignmentHistory: AssignmentHistoryDto[];
  activity: TaskActivityDto[];
  qcReviews: QCReviewDto[];
  subTaskIds: number[];
  /** Task numbers of unfinished work this task waits on. Non-empty blocks the timer. */
  blockedBy: string[];
  rowVersion?: string | null;
}

export interface PauseReasonDto {
  id: number;
  name: string;
  requiresComment: boolean;
  isBlocker: boolean;
}

export interface AssignableUserDto {
  id: number;
  userName: string;
  displayName: string;
  workforceState: WorkforceState;
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
  breakdown: TaskTimeDto[];
}

export interface DailyTeamReportDto {
  date: string;
  peopleOnShift: number;
  totalShiftTime: string;
  totalProductiveTime: string;
  tasksCompleted: number;
  users: DailyUserReportDto[];
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
