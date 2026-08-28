import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AcceptanceCriteriaDto, ActiveWorkforceDto, ActivityEventDto, AssignableUserDto,
  AssignmentCandidateDto, AttachmentDto, FormDto, FormOptionDto, FormSurfaceDto,
  FormSurfaceOptionDto, ModuleDto,
  AttachmentKind,
  AuditLogDto, ClientOptionDto, ModuleOptionDto, HomeDashboardDto, QuickWorkDto, StartQuickWorkDto,
  CreateRequestBatchDto, RequestBatchDetailDto, RequestBatchSummaryDto, ApproveTogetherDto,
  FinishQuickWorkDto, PromoteQuickWorkDto, ClosureChecklistDto, StatusCountDto, TriageResultDto, CoordinatorDashboardDto, CommentCategory, DailyTeamReportDto,
  DailyTimelineDto, DailyUserReportDto, DependencyType, ManagementDashboardDto, NotificationDto,
  PagedResult, PauseReasonDto, Priority, QCReviewDto, QCResult, RequestDetailDto,
  RequesterDashboardDto, RequestStatus, RequestSummaryDto, RequestType, RequestedUrgency, RoleDto,
  ScopeChangeDto, ShiftSessionDto, TaskCommentDto, TaskDependencyGraphDto, TaskDetailDto,
  TaskSummaryDto, TriageOutcome, UserDto, WorkSessionDto, WorkTaskStatus, WorkforceState,
  WorkforceStatusDto, WorkloadDto,
  SetupClientDto, SetupDepartmentDto, SetupTeamDto, SetupPauseReasonDto, RoleDetailDto,
  FilterOptionsDto,
  PauseCategory,
  VerificationStatus, VerificationSummaryDto, VerificationDetailDto, AssignableCheckerDto,
  CreateVerificationDto, SendForVerificationDto, AssignVerificationDto,
  RecordVerificationResultDto, CancelVerificationDto,
} from './models';

/**
 * The grid filter row's contribution to a query string: `col[title]`, `col[client]`, and so on.
 *
 * Loosely typed on purpose — the keys are the grid's own column names, and the service that owns
 * the table decides what each one means. `ColumnFilterState.asObject()` produces exactly this.
 */
export type ColumnFilterParams = Record<`col[${string}]`, string | undefined>;

/** Drops null/undefined so an untouched filter never becomes `?status=null`. */
function params(source: Record<string, unknown>): HttpParams {
  let result = new HttpParams();
  for (const [key, value] of Object.entries(source)) {
    if (value !== null && value !== undefined && value !== '') {
      result = result.set(key, String(value));
    }
  }
  return result;
}

/**
 * The single place the client talks to the API.
 *
 * Grouped by the resource it addresses rather than split into a service per screen, because several
 * screens share the same calls and duplicating a URL is how a rename becomes a runtime 404.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  // --- identity ------------------------------------------------------------------------------

  me(): Observable<UserDto> {
    return this.http.get<UserDto>('/api/auth/me');
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<void>('/api/auth/change-password', { currentPassword, newPassword });
  }

  users(filter: {
    search?: string; isActive?: boolean; page?: number; pageSize?: number;
  } & ColumnFilterParams = {}): Observable<PagedResult<UserDto>> {
    return this.http.get<PagedResult<UserDto>>('/api/users', { params: params(filter) });
  }

  user(id: number): Observable<UserDto> {
    return this.http.get<UserDto>(`/api/users/${id}`);
  }

  createUser(body: {
    userName: string; email?: string; displayName: string; password: string; roles: string[];
  }, context?: HttpContext): Observable<UserDto> {
    return this.http.post<UserDto>('/api/users', body, { context });
  }

  updateUser(
    id: number,
    body: {
      userName: string; displayName: string; email?: string | null;
      /** Blank leaves it unchanged. Setting one signs the person out everywhere. */
      newPassword?: string | null;
      departmentId?: number | null; teamId?: number | null;
    },
    context?: HttpContext,
  ): Observable<UserDto> {
    return this.http.put<UserDto>(`/api/users/${id}`, body, { context });
  }

  /** The caller's own name and email. Username, roles and access are an administrator's. */
  updateMyProfile(
    body: { displayName: string; email?: string | null },
    context?: HttpContext,
  ): Observable<UserDto> {
    return this.http.put<UserDto>('/api/auth/me', body, { context });
  }

  setUserActive(id: number, isActive: boolean, context?: HttpContext): Observable<UserDto> {
    return this.http.put<UserDto>(`/api/users/${id}/active`, { isActive }, { context });
  }

  setUserRoles(id: number, roles: string[], context?: HttpContext): Observable<UserDto> {
    return this.http.put<UserDto>(`/api/users/${id}/roles`, { roles }, { context });
  }

  resetPassword(id: number, newPassword: string, context?: HttpContext): Observable<void> {
    return this.http.post<void>(`/api/users/${id}/reset-password`, { newPassword }, { context });
  }

  roles(): Observable<RoleDto[]> {
    return this.http.get<RoleDto[]>('/api/roles');
  }

  permissionCatalog(): Observable<string[]> {
    return this.http.get<string[]>('/api/roles/permissions');
  }

  // --- reference data ------------------------------------------------------------------------

  /** Known clients: the name feeds the type-ahead, the id feeds the list filters. */
  clients(search?: string): Observable<ClientOptionDto[]> {
    return this.http.get<ClientOptionDto[]>('/api/lookups/clients', { params: params({ search }) });
  }

  modules(search?: string): Observable<ModuleOptionDto[]> {
    return this.http.get<ModuleOptionDto[]>('/api/lookups/modules', { params: params({ search }) });
  }

  /**
   * The product-catalog pickers (PRODUCT-CORE §5). Note that neither takes a client id, and
   * neither can be given one: the catalog describes the product, not a client's copy of it.
   */
  formOptions(moduleId?: number | null, search?: string): Observable<FormOptionDto[]> {
    return this.http.get<FormOptionDto[]>(
      '/api/lookups/forms', { params: params({ moduleId, search }) });
  }

  formSurfaceOptions(formId?: number | null, search?: string): Observable<FormSurfaceOptionDto[]> {
    return this.http.get<FormSurfaceOptionDto[]>(
      '/api/lookups/form-surfaces', { params: params({ formId, search }) });
  }

  // --- the catalog, administered --------------------------------------------------------------

  setupModules(): Observable<ModuleDto[]> {
    return this.http.get<ModuleDto[]>('/api/setup/modules');
  }

  createModule(name: string, context?: HttpContext): Observable<ModuleDto> {
    return this.http.post<ModuleDto>('/api/setup/modules', { name }, { context });
  }

  updateModule(id: number, name: string, context?: HttpContext): Observable<ModuleDto> {
    return this.http.put<ModuleDto>(`/api/setup/modules/${id}`, { name }, { context });
  }

  setModuleActive(id: number, isActive: boolean): Observable<ModuleDto> {
    return this.http.put<ModuleDto>(`/api/setup/modules/${id}/active`, { isActive });
  }

  setupForms(): Observable<FormDto[]> {
    return this.http.get<FormDto[]>('/api/setup/forms');
  }

  createForm(name: string, moduleId: number, context?: HttpContext): Observable<FormDto> {
    return this.http.post<FormDto>('/api/setup/forms', { name, moduleId }, { context });
  }

  updateForm(id: number, name: string, moduleId: number, context?: HttpContext): Observable<FormDto> {
    return this.http.put<FormDto>(`/api/setup/forms/${id}`, { name, moduleId }, { context });
  }

  setFormActive(id: number, isActive: boolean): Observable<FormDto> {
    return this.http.put<FormDto>(`/api/setup/forms/${id}/active`, { isActive });
  }

  setupFormSurfaces(): Observable<FormSurfaceDto[]> {
    return this.http.get<FormSurfaceDto[]>('/api/setup/form-surfaces');
  }

  createFormSurface(name: string, formId: number, context?: HttpContext): Observable<FormSurfaceDto> {
    return this.http.post<FormSurfaceDto>('/api/setup/form-surfaces', { name, formId }, { context });
  }

  updateFormSurface(
    id: number, name: string, formId: number, context?: HttpContext,
  ): Observable<FormSurfaceDto> {
    return this.http.put<FormSurfaceDto>(
      `/api/setup/form-surfaces/${id}`, { name, formId }, { context });
  }

  setFormSurfaceActive(id: number, isActive: boolean): Observable<FormSurfaceDto> {
    return this.http.put<FormSurfaceDto>(`/api/setup/form-surfaces/${id}/active`, { isActive });
  }

  // --- requests ------------------------------------------------------------------------------

  requestFilterOptions(filter: {
    view?: string; mine?: boolean;
  } & ColumnFilterParams): Observable<FilterOptionsDto> {
    return this.http.get<FilterOptionsDto>('/api/requests/filter-options', { params: params(filter) });
  }

  requests(filter: {
    status?: RequestStatus; view?: string; mine?: boolean; search?: string; clientId?: number;
    sortBy?: string; sortDescending?: boolean; page?: number; pageSize?: number;
  } & ColumnFilterParams): Observable<PagedResult<RequestSummaryDto>> {
    return this.http.get<PagedResult<RequestSummaryDto>>('/api/requests', { params: params(filter) });
  }

  request(id: number): Observable<RequestDetailDto> {
    return this.http.get<RequestDetailDto>(`/api/requests/${id}`);
  }

  createRequest(body: {
    title: string; description: string; type: RequestType; requestedUrgency: RequestedUrgency;
    businessImpact?: string; expectedResult?: string; currentResult?: string;
    reproductionSteps?: string; targetDate?: string | null;
    clientName?: string | null;
    /** Where in the product, if the requester knew. Refined at triage (PRODUCT-CORE §5). */
    moduleId?: number; formId?: number;
  }, context?: HttpContext): Observable<RequestDetailDto> {
    return this.http.post<RequestDetailDto>('/api/requests', body, { context });
  }

  updateRequest(id: number, body: Record<string, unknown>, context?: HttpContext): Observable<RequestDetailDto> {
    return this.http.put<RequestDetailDto>(`/api/requests/${id}`, body, { context });
  }

  /**
   * A point found in a later round (PRODUCT-CORE §6). Its own request, linked back, carrying the
   * shared client and product location — and deliberately not touching what is already running.
   */
  followUpRequest(
    originalId: number,
    body: { title: string; description?: string; type?: RequestType; requestedUrgency?: RequestedUrgency },
    context?: HttpContext,
  ): Observable<RequestDetailDto> {
    return this.http.post<RequestDetailDto>(
      `/api/requests/${originalId}/follow-up`, body, { context });
  }

  reviewQueue(page = 1, pageSize = 25): Observable<PagedResult<RequestSummaryDto>> {
    return this.http.get<PagedResult<RequestSummaryDto>>('/api/requests/review-queue', {
      params: params({ page, pageSize }),
    });
  }

  startReview(id: number, context?: HttpContext): Observable<RequestDetailDto> {
    return this.http.post<RequestDetailDto>(`/api/requests/${id}/start-review`, {}, { context });
  }

  triage(id: number, body: {
    outcome: TriageOutcome; reason?: string; approvedPriority?: Priority;
    estimatedEffortHours?: number; dueDate?: string | null; acceptanceCriteria?: string;
    duplicateOfRequestId?: number;
    clientName?: string | null;
    /** Where in the product this is (PRODUCT-CORE §5). Set when approving; never a client id. */
    moduleId?: number; formId?: number; formSurfaceId?: number;
    /** Required when the outcome is SendForVerification. Produces a check, never a task. */
    verification?: SendForVerificationDto;
  }, context?: HttpContext): Observable<TriageResultDto> {
    return this.http.post<TriageResultDto>(`/api/requests/${id}/triage`, body, { context });
  }

  // --- verifications -------------------------------------------------------------------------
  //
  // Assigned investigation. Nothing here creates work: recording a result hands the request back
  // to a reviewer, and approving it stays a separate call to `triage` above.

  verifications(filter: {
    status?: VerificationStatus; mineOnly?: boolean; page?: number; pageSize?: number;
  }): Observable<PagedResult<VerificationSummaryDto>> {
    return this.http.get<PagedResult<VerificationSummaryDto>>(
      '/api/verifications', { params: params(filter) });
  }

  myVerificationQueue(): Observable<VerificationSummaryDto[]> {
    return this.http.get<VerificationSummaryDto[]>('/api/verifications/my-queue');
  }

  verification(id: number): Observable<VerificationDetailDto> {
    return this.http.get<VerificationDetailDto>(`/api/verifications/${id}`);
  }

  assignableCheckers(): Observable<AssignableCheckerDto[]> {
    return this.http.get<AssignableCheckerDto[]>('/api/verifications/assignable-checkers');
  }

  createVerification(
    body: CreateVerificationDto, context?: HttpContext,
  ): Observable<VerificationDetailDto> {
    return this.http.post<VerificationDetailDto>('/api/verifications', body, { context });
  }

  assignVerification(
    id: number, body: AssignVerificationDto, context?: HttpContext,
  ): Observable<VerificationDetailDto> {
    return this.http.put<VerificationDetailDto>(`/api/verifications/${id}/assignee`, body, { context });
  }

  /** A checker taking an unclaimed check. Refused once somebody holds it — that needs assigning. */
  claimVerification(id: number, context?: HttpContext): Observable<VerificationDetailDto> {
    return this.http.post<VerificationDetailDto>(`/api/verifications/${id}/claim`, {}, { context });
  }

  startVerification(id: number, context?: HttpContext): Observable<VerificationDetailDto> {
    return this.http.post<VerificationDetailDto>(`/api/verifications/${id}/start`, {}, { context });
  }

  recordVerificationResult(
    id: number, body: RecordVerificationResultDto, context?: HttpContext,
  ): Observable<VerificationDetailDto> {
    return this.http.post<VerificationDetailDto>(`/api/verifications/${id}/result`, body, { context });
  }

  cancelVerification(
    id: number, body: CancelVerificationDto, context?: HttpContext,
  ): Observable<VerificationDetailDto> {
    return this.http.post<VerificationDetailDto>(`/api/verifications/${id}/cancel`, body, { context });
  }

  /**
   * Evidence for an investigation. The kind is fixed server-side, and only the assigned checker is
   * accepted — the same shape of rule as completion proof on a task.
   */
  uploadVerificationAttachment(verificationId: number, file: File): Observable<AttachmentDto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<AttachmentDto>(`/api/verifications/${verificationId}/attachments`, form);
  }

  answerClarification(clarificationId: number, answer: string, context?: HttpContext): Observable<RequestDetailDto> {
    return this.http.post<RequestDetailDto>(
      `/api/requests/clarifications/${clarificationId}/answer`, { answer }, { context });
  }

  uploadRequestAttachment(requestId: number, file: File): Observable<AttachmentDto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<AttachmentDto>(`/api/requests/${requestId}/attachments`, form);
  }

  /**
   * `kind` says what the file is for. The server decides who may claim what: only the person
   * responsible for the work may attach proof of it, and only a checker may attach evidence to a
   * check — so a caller that gets this wrong is refused rather than quietly filed under the wrong
   * heading.
   */
  uploadTaskAttachment(
    taskId: number, file: File, kind: AttachmentKind = 'General',
  ): Observable<AttachmentDto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<AttachmentDto>(
      `/api/tasks/${taskId}/attachments`, form, { params: { kind } });
  }

  downloadAttachment(id: number): Observable<Blob> {
    return this.http.get(`/api/attachments/${id}`, { responseType: 'blob' });
  }

  // --- tasks ---------------------------------------------------------------------------------

  taskFilterOptions(filter: {
    view?: string; openOnly?: boolean;
  } & ColumnFilterParams): Observable<FilterOptionsDto> {
    return this.http.get<FilterOptionsDto>('/api/tasks/filter-options', { params: params(filter) });
  }

  tasks(filter: {
    status?: WorkTaskStatus; view?: string; priority?: Priority; assigneeUserId?: number;
    unassigned?: boolean;
    clientId?: number; openOnly?: boolean; search?: string;
    sortBy?: string; sortDescending?: boolean; page?: number; pageSize?: number;
  } & ColumnFilterParams): Observable<PagedResult<TaskSummaryDto>> {
    return this.http.get<PagedResult<TaskSummaryDto>>('/api/tasks', { params: params(filter) });
  }

  taskStatusCounts(filter: { clientId?: number; search?: string; openOnly?: boolean } = {}):
    Observable<StatusCountDto[]> {
    return this.http.get<StatusCountDto[]>('/api/tasks/status-counts', { params: params(filter) });
  }

  requestStatusCounts(filter: {
    type?: RequestType; clientId?: number; search?: string; mine?: boolean;
  } = {}): Observable<StatusCountDto[]> {
    return this.http.get<StatusCountDto[]>('/api/requests/status-counts', { params: params(filter) });
  }

  task(id: number): Observable<TaskDetailDto> {
    return this.http.get<TaskDetailDto>(`/api/tasks/${id}`);
  }

  myQueue(): Observable<TaskSummaryDto[]> {
    return this.http.get<TaskSummaryDto[]>('/api/tasks/my-queue');
  }

  reorderQueue(taskIdsInOrder: number[]): Observable<void> {
    return this.http.put<void>('/api/tasks/my-queue/order', { taskIdsInOrder });
  }

  assignmentQueue(page = 1, pageSize = 25): Observable<PagedResult<TaskSummaryDto>> {
    return this.http.get<PagedResult<TaskSummaryDto>>('/api/tasks/assignment-queue', {
      params: params({ page, pageSize }),
    });
  }

  qcQueue(page = 1, pageSize = 25): Observable<PagedResult<TaskSummaryDto>> {
    return this.http.get<PagedResult<TaskSummaryDto>>('/api/tasks/qc-queue', {
      params: params({ page, pageSize }),
    });
  }

  workload(): Observable<WorkloadDto[]> {
    return this.http.get<WorkloadDto[]>('/api/tasks/workload');
  }

  assignableUsers(): Observable<AssignableUserDto[]> {
    return this.http.get<AssignableUserDto[]>('/api/tasks/assignable-users');
  }

  /** Who this task could go to, with the facts to decide on. See AssignmentCandidateDto. */
  assignmentCandidates(taskId: number): Observable<AssignmentCandidateDto[]> {
    return this.http.get<AssignmentCandidateDto[]>(`/api/tasks/${taskId}/assignment-candidates`);
  }

  pauseReasons(): Observable<PauseReasonDto[]> {
    return this.http.get<PauseReasonDto[]>('/api/tasks/pause-reasons');
  }

  activeSession(): Observable<WorkSessionDto | null> {
    return this.http.get<WorkSessionDto | null>('/api/tasks/active-session');
  }

  transition(id: number, body: {
    to: WorkTaskStatus; reason?: string; isOverride?: boolean; idempotencyKey?: string;
  }, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/transition`, body, { context });
  }

  assign(id: number, assigneeUserId: number | null, reason?: string, rowVersion?: string | null, context?: HttpContext):
    Observable<TaskDetailDto> {
    return this.http.put<TaskDetailDto>(`/api/tasks/${id}/assignee`, {
      assigneeUserId, reason, rowVersion,
    }, { context });
  }

  setTaskRoles(id: number, reviewerUserId: number | null, qcUserId: number | null, context?: HttpContext):
    Observable<TaskDetailDto> {
    return this.http.put<TaskDetailDto>(`/api/tasks/${id}/roles`, { reviewerUserId, qcUserId }, { context });
  }

  addCollaborator(id: number, userId: number, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/collaborators`, { userId }, { context });
  }

  removeCollaborator(id: number, userId: number, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.delete<TaskDetailDto>(`/api/tasks/${id}/collaborators/${userId}`, { context });
  }

  updateTaskDetails(id: number, body: {
    priority?: Priority; estimatedEffortHours?: number; dueDate?: string | null;
    acceptanceCriteria?: string; resolution?: string; progressPercent?: number;
  }): Observable<TaskDetailDto> {
    return this.http.patch<TaskDetailDto>(`/api/tasks/${id}`, body);
  }

  // --- the timer -----------------------------------------------------------------------------

  startWork(id: number, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/start`, {}, { context });
  }

  pauseWork(id: number, pauseReasonId?: number, comment?: string, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/pause`, { pauseReasonId, comment }, { context });
  }

  blockWork(id: number, pauseReasonId?: number, comment?: string, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/block`, { pauseReasonId, comment }, { context });
  }

  completeWork(id: number, resolution?: string, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/complete`, { resolution }, { context });
  }

  interrupt(taskId: number, reason?: string, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>('/api/tasks/interrupt', { taskId, reason }, { context });
  }

  // --- QC & closure --------------------------------------------------------------------------

  startQC(id: number, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/qc/start`, {}, { context });
  }

  submitQC(id: number, body: {
    result: QCResult; comments?: string; environment?: string; buildVersion?: string;
    // met: true = pass, false = fail, null = not applicable. Omitting a criterion entirely means
    // "not answered yet", which the server rejects on a pass — that distinction is deliberate.
    criteria: { index: number; met: boolean | null; note?: string }[];
  }, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/qc/review`, body, { context });
  }

  qcHistory(id: number): Observable<QCReviewDto[]> {
    return this.http.get<QCReviewDto[]>(`/api/tasks/${id}/qc`);
  }

  acceptanceCriteria(id: number): Observable<AcceptanceCriteriaDto> {
    return this.http.get<AcceptanceCriteriaDto>(`/api/tasks/${id}/acceptance-criteria`);
  }

  closureCheck(id: number): Observable<ClosureChecklistDto> {
    return this.http.get<ClosureChecklistDto>(`/api/tasks/${id}/closure-check`);
  }

  closeTask(id: number, resolution?: string, reason?: string, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/close`, { resolution, reason }, { context });
  }

  reopenTask(id: number, reason: string, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/reopen`, { reason }, { context });
  }

  /**
   * The requester closing their own loop (PRODUCT-CORE §7). Neither is permission-gated: the
   * server decides on the record whether you are the person who asked for this work.
   */
  acceptFix(id: number, note?: string, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/accept`, { note }, { context });
  }

  rejectFix(id: number, reason: string, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/reject`, { reason }, { context });
  }

  // --- collaboration -------------------------------------------------------------------------

  comments(id: number): Observable<TaskCommentDto[]> {
    return this.http.get<TaskCommentDto[]>(`/api/tasks/${id}/comments`);
  }

  addComment(id: number, body: {
    body: string; category: CommentCategory; visibleToRequester?: boolean | null;
  }, context?: HttpContext): Observable<TaskCommentDto> {
    return this.http.post<TaskCommentDto>(`/api/tasks/${id}/comments`, body, { context });
  }

  dependencies(id: number): Observable<TaskDependencyGraphDto> {
    return this.http.get<TaskDependencyGraphDto>(`/api/tasks/${id}/dependencies`);
  }

  addDependency(id: number, relatedTaskId: number, type: DependencyType, context?: HttpContext):
    Observable<TaskDependencyGraphDto> {
    return this.http.post<TaskDependencyGraphDto>(`/api/tasks/${id}/dependencies`, {
      relatedTaskId, type,
    }, { context });
  }

  removeDependency(
    id: number, dependencyId: number, context?: HttpContext,
  ): Observable<TaskDependencyGraphDto> {
    return this.http.delete<TaskDependencyGraphDto>(
      `/api/tasks/${id}/dependencies/${dependencyId}`, { context });
  }

  subtasks(id: number): Observable<PagedResult<TaskSummaryDto>> {
    return this.http.get<PagedResult<TaskSummaryDto>>(`/api/tasks/${id}/subtasks`);
  }

  createSubtask(id: number, body: {
    title: string; description: string; priority?: Priority; estimatedEffortHours?: number;
    dueDate?: string | null; acceptanceCriteria?: string; assigneeUserId?: number | null;
    isRequired?: boolean;
  }, context?: HttpContext): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/subtasks`, body, { context });
  }

  scopeChanges(id: number): Observable<ScopeChangeDto[]> {
    return this.http.get<ScopeChangeDto[]>(`/api/tasks/${id}/scope-changes`);
  }

  requestScopeChange(id: number, body: {
    description: string; reason?: string; estimatedImpactHours?: number;
    deadlineImpact?: string | null;
  }): Observable<ScopeChangeDto> {
    return this.http.post<ScopeChangeDto>(`/api/tasks/${id}/scope-changes`, body);
  }

  approveScopeChange(scopeChangeId: number, context?: HttpContext): Observable<ScopeChangeDto> {
    return this.http.post<ScopeChangeDto>(
      `/api/tasks/scope-changes/${scopeChangeId}/approve`, {}, { context });
  }

  // --- shifts & workforce ----------------------------------------------------------------------

  myShiftStatus(): Observable<WorkforceStatusDto> {
    return this.http.get<WorkforceStatusDto>('/api/shifts/current');
  }

  // --- request batches ------------------------------------------------------------------------

  createBatch(body: CreateRequestBatchDto, context?: HttpContext): Observable<RequestBatchDetailDto> {
    return this.http.post<RequestBatchDetailDto>('/api/requests/batches', body, { context });
  }

  batch(id: number): Observable<RequestBatchDetailDto> {
    return this.http.get<RequestBatchDetailDto>(`/api/requests/batches/${id}`);
  }

  myBatches(page = 1, pageSize = 25): Observable<PagedResult<RequestBatchSummaryDto>> {
    return this.http.get<PagedResult<RequestBatchSummaryDto>>('/api/requests/batches/mine', {
      params: params({ page, pageSize }),
    });
  }

  batchReviewQueue(page = 1, pageSize = 25): Observable<PagedResult<RequestBatchSummaryDto>> {
    return this.http.get<PagedResult<RequestBatchSummaryDto>>('/api/requests/batches/review-queue', {
      params: params({ page, pageSize }),
    });
  }

  approveTogether(
    batchId: number, body: ApproveTogetherDto, context?: HttpContext,
  ): Observable<TriageResultDto> {
    return this.http.post<TriageResultDto>(
      `/api/requests/batches/${batchId}/approve-together`, body, { context });
  }

  uploadBatchAttachment(batchId: number, file: File): Observable<AttachmentDto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<AttachmentDto>(`/api/requests/batches/${batchId}/attachments`, form);
  }

  // --- quick work ---------------------------------------------------------------------------

  /** 204 when nothing is running, which `HttpClient` gives us as null. */
  activeQuickWork(): Observable<QuickWorkDto | null> {
    return this.http.get<QuickWorkDto | null>('/api/quick-work/active');
  }

  quickWorkForDay(date?: string): Observable<QuickWorkDto[]> {
    return this.http.get<QuickWorkDto[]>('/api/quick-work', {
      params: date ? { date } : {},
    });
  }

  startQuickWork(body: StartQuickWorkDto, context?: HttpContext): Observable<QuickWorkDto> {
    return this.http.post<QuickWorkDto>('/api/quick-work', body, { context });
  }

  finishQuickWork(
    id: number, body: FinishQuickWorkDto, context?: HttpContext,
  ): Observable<QuickWorkDto> {
    return this.http.post<QuickWorkDto>(`/api/quick-work/${id}/finish`, body, { context });
  }

  cancelQuickWork(id: number, context?: HttpContext): Observable<QuickWorkDto> {
    return this.http.post<QuickWorkDto>(`/api/quick-work/${id}/cancel`, {}, { context });
  }

  promoteQuickWork(
    id: number, body: PromoteQuickWorkDto, context?: HttpContext,
  ): Observable<QuickWorkDto> {
    return this.http.post<QuickWorkDto>(`/api/quick-work/${id}/promote`, body, { context });
  }

  startShift(context?: HttpContext): Observable<WorkforceStatusDto> {
    return this.http.post<WorkforceStatusDto>('/api/shifts/start', {}, { context });
  }

  endShift(context?: HttpContext, note?: string): Observable<WorkforceStatusDto> {
    return this.http.post<WorkforceStatusDto>('/api/shifts/end', { note }, { context });
  }

  setWorkforceState(
    state: WorkforceState, note?: string, context?: HttpContext,
  ): Observable<WorkforceStatusDto> {
    return this.http.put<WorkforceStatusDto>('/api/shifts/state', { state, note }, { context });
  }

  myTimeline(date?: string): Observable<DailyTimelineDto> {
    return this.http.get<DailyTimelineDto>('/api/shifts/timeline', { params: params({ date }) });
  }

  myActivity(date?: string): Observable<ActivityEventDto[]> {
    return this.http.get<ActivityEventDto[]>('/api/shifts/activity', { params: params({ date }) });
  }

  myShiftHistory(page = 1, pageSize = 25): Observable<PagedResult<ShiftSessionDto>> {
    return this.http.get<PagedResult<ShiftSessionDto>>('/api/shifts/history', {
      params: params({ page, pageSize }),
    });
  }

  activeWorkforce(): Observable<ActiveWorkforceDto> {
    return this.http.get<ActiveWorkforceDto>('/api/workforce/active');
  }

  userTimeline(userId: number, date?: string): Observable<DailyTimelineDto> {
    return this.http.get<DailyTimelineDto>(`/api/workforce/${userId}/timeline`, {
      params: params({ date }),
    });
  }

  forceEndShift(userId: number, reason: string, context?: HttpContext): Observable<unknown> {
    return this.http.post(`/api/workforce/${userId}/end-shift`, { reason }, { context });
  }

  // --- dashboards & reports ----------------------------------------------------------------------

  /** The home screen: what is waiting on me, and what has happened. */
  homeDashboard(): Observable<HomeDashboardDto> {
    return this.http.get<HomeDashboardDto>('/api/dashboards/home');
  }

  requesterDashboard(): Observable<RequesterDashboardDto> {
    return this.http.get<RequesterDashboardDto>('/api/dashboards/requester');
  }

  workerDashboard(): Observable<import('./models').WorkerDashboardDto> {
    return this.http.get<import('./models').WorkerDashboardDto>('/api/dashboards/worker');
  }

  coordinatorDashboard(): Observable<CoordinatorDashboardDto> {
    return this.http.get<CoordinatorDashboardDto>('/api/dashboards/coordinator');
  }

  managementDashboard(from?: string, to?: string): Observable<ManagementDashboardDto> {
    return this.http.get<ManagementDashboardDto>('/api/dashboards/management', {
      params: params({ from, to }),
    });
  }

  myDailyReport(date?: string): Observable<DailyUserReportDto> {
    return this.http.get<DailyUserReportDto>('/api/reports/me/daily', { params: params({ date }) });
  }

  teamDailyReport(date?: string): Observable<DailyTeamReportDto> {
    return this.http.get<DailyTeamReportDto>('/api/reports/team/daily', { params: params({ date }) });
  }

  teamDailyCsv(date?: string): Observable<Blob> {
    return this.http.get('/api/reports/team/daily.csv', {
      params: params({ date }),
      responseType: 'blob',
    });
  }

  /**
   * The same day as a document rather than a spreadsheet — header, summary, work detail, quick
   * work, interruptions, notes and page numbers, rendered server-side.
   */
  teamDailyPdf(date?: string): Observable<Blob> {
    return this.http.get('/api/reports/team/daily.pdf', {
      params: params({ date }),
      responseType: 'blob',
    });
  }

  myDailyPdf(date?: string): Observable<Blob> {
    return this.http.get('/api/reports/me/daily.pdf', {
      params: params({ date }),
      responseType: 'blob',
    });
  }

  userDailyPdf(userId: number, date?: string): Observable<Blob> {
    return this.http.get(`/api/reports/users/${userId}/daily.pdf`, {
      params: params({ date }),
      responseType: 'blob',
    });
  }

  // --- notifications & audit -----------------------------------------------------------------------

  notifications(unreadOnly = false, page = 1, pageSize = 25): Observable<PagedResult<NotificationDto>> {
    return this.http.get<PagedResult<NotificationDto>>('/api/notifications', {
      params: params({ unreadOnly, page, pageSize }),
    });
  }

  unreadCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>('/api/notifications/unread-count');
  }

  markRead(notificationIds: number[]): Observable<void> {
    return this.http.post<void>('/api/notifications/read', { notificationIds });
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>('/api/notifications/read-all', {});
  }

  audit(filter: {
    action?: string; entityType?: string; entityId?: number; actorUserId?: number;
    from?: string; to?: string; page?: number; pageSize?: number;
  }): Observable<PagedResult<AuditLogDto>> {
    return this.http.get<PagedResult<AuditLogDto>>('/api/audit', { params: params(filter) });
  }

  auditActions(): Observable<string[]> {
    return this.http.get<string[]>('/api/audit/actions');
  }

  // --- administrator setup ---------------------------------------------------------------------
  //
  // Reference data. Every list has create / update / set-active and deliberately no delete: these
  // rows are pointed at by history, so they are retired rather than removed. Roles are the one
  // exception, and only while nobody holds them.

  setupClients(): Observable<SetupClientDto[]> {
    return this.http.get<SetupClientDto[]>('/api/setup/clients');
  }

  createClient(body: { name: string; code?: string | null }, context?: HttpContext) {
    return this.http.post<SetupClientDto>('/api/setup/clients', body, { context });
  }

  updateClient(id: number, body: { name: string; code?: string | null }, context?: HttpContext) {
    return this.http.put<SetupClientDto>(`/api/setup/clients/${id}`, body, { context });
  }

  setClientActive(id: number, isActive: boolean, context?: HttpContext) {
    return this.http.put<SetupClientDto>(`/api/setup/clients/${id}/active`, { isActive }, { context });
  }

  setupDepartments(): Observable<SetupDepartmentDto[]> {
    return this.http.get<SetupDepartmentDto[]>('/api/setup/departments');
  }

  createDepartment(body: { name: string }, context?: HttpContext) {
    return this.http.post<SetupDepartmentDto>('/api/setup/departments', body, { context });
  }

  updateDepartment(id: number, body: { name: string }, context?: HttpContext) {
    return this.http.put<SetupDepartmentDto>(`/api/setup/departments/${id}`, body, { context });
  }

  setDepartmentActive(id: number, isActive: boolean, context?: HttpContext) {
    return this.http.put<SetupDepartmentDto>(
      `/api/setup/departments/${id}/active`, { isActive }, { context });
  }

  setupTeams(): Observable<SetupTeamDto[]> {
    return this.http.get<SetupTeamDto[]>('/api/setup/teams');
  }

  createTeam(body: { name: string; departmentId?: number | null }, context?: HttpContext) {
    return this.http.post<SetupTeamDto>('/api/setup/teams', body, { context });
  }

  updateTeam(id: number, body: { name: string; departmentId?: number | null }, context?: HttpContext) {
    return this.http.put<SetupTeamDto>(`/api/setup/teams/${id}`, body, { context });
  }

  setTeamActive(id: number, isActive: boolean, context?: HttpContext) {
    return this.http.put<SetupTeamDto>(`/api/setup/teams/${id}/active`, { isActive }, { context });
  }

  setupPauseReasons(): Observable<SetupPauseReasonDto[]> {
    return this.http.get<SetupPauseReasonDto[]>('/api/setup/pause-reasons');
  }

  createPauseReason(body: SavePauseReasonBody, context?: HttpContext) {
    return this.http.post<SetupPauseReasonDto>('/api/setup/pause-reasons', body, { context });
  }

  updatePauseReason(id: number, body: SavePauseReasonBody, context?: HttpContext) {
    return this.http.put<SetupPauseReasonDto>(`/api/setup/pause-reasons/${id}`, body, { context });
  }

  setPauseReasonActive(id: number, isActive: boolean, context?: HttpContext) {
    return this.http.put<SetupPauseReasonDto>(
      `/api/setup/pause-reasons/${id}/active`, { isActive }, { context });
  }

  setupRoles(): Observable<RoleDetailDto[]> {
    return this.http.get<RoleDetailDto[]>('/api/setup/roles');
  }

  createRole(body: { name: string; description?: string | null }, context?: HttpContext) {
    return this.http.post<RoleDetailDto>('/api/setup/roles', body, { context });
  }

  updateRole(id: number, body: { name: string; description?: string | null }, context?: HttpContext) {
    return this.http.put<RoleDetailDto>(`/api/setup/roles/${id}`, body, { context });
  }

  setRolePermissions(id: number, permissions: string[], context?: HttpContext) {
    return this.http.put<RoleDetailDto>(
      `/api/setup/roles/${id}/permissions`, { permissions }, { context });
  }

  deleteRole(id: number, context?: HttpContext): Observable<void> {
    return this.http.delete<void>(`/api/setup/roles/${id}`, { context });
  }
}

/** What every pause-reason write sends. Named because three call sites repeat it. */
export interface SavePauseReasonBody {
  name: string;
  requiresComment: boolean;
  isBlocker: boolean;
  category: PauseCategory;
  awayState?: WorkforceState | null;
}
