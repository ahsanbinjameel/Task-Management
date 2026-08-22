import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AcceptanceCriteriaDto, ActiveWorkforceDto, ActivityEventDto, AssignableUserDto, AttachmentDto,
  AuditLogDto, ClosureChecklistDto, CoordinatorDashboardDto, CommentCategory, DailyTeamReportDto,
  DailyTimelineDto, DailyUserReportDto, DependencyType, ManagementDashboardDto, NotificationDto,
  PagedResult, PauseReasonDto, Priority, QCReviewDto, QCResult, RequestDetailDto,
  RequesterDashboardDto, RequestStatus, RequestSummaryDto, RequestType, RequestedUrgency, RoleDto,
  ScopeChangeDto, ShiftSessionDto, TaskCommentDto, TaskDependencyGraphDto, TaskDetailDto,
  TaskSummaryDto, TriageOutcome, UserDto, WorkSessionDto, WorkTaskStatus, WorkforceState,
  WorkforceStatusDto, WorkloadDto,
} from './models';

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

  users(filter: { search?: string; isActive?: boolean; page?: number; pageSize?: number } = {}):
    Observable<PagedResult<UserDto>> {
    return this.http.get<PagedResult<UserDto>>('/api/users', { params: params(filter) });
  }

  user(id: number): Observable<UserDto> {
    return this.http.get<UserDto>(`/api/users/${id}`);
  }

  createUser(body: {
    userName: string; email: string; displayName: string; password: string; roles: string[];
  }): Observable<UserDto> {
    return this.http.post<UserDto>('/api/users', body);
  }

  setUserActive(id: number, isActive: boolean): Observable<UserDto> {
    return this.http.put<UserDto>(`/api/users/${id}/active`, { isActive });
  }

  setUserRoles(id: number, roles: string[]): Observable<UserDto> {
    return this.http.put<UserDto>(`/api/users/${id}/roles`, { roles });
  }

  resetPassword(id: number, newPassword: string): Observable<void> {
    return this.http.post<void>(`/api/users/${id}/reset-password`, { newPassword });
  }

  roles(): Observable<RoleDto[]> {
    return this.http.get<RoleDto[]>('/api/roles');
  }

  permissionCatalog(): Observable<string[]> {
    return this.http.get<string[]>('/api/roles/permissions');
  }

  // --- requests ------------------------------------------------------------------------------

  requests(filter: {
    status?: RequestStatus; mine?: boolean; search?: string; page?: number; pageSize?: number;
  }): Observable<PagedResult<RequestSummaryDto>> {
    return this.http.get<PagedResult<RequestSummaryDto>>('/api/requests', { params: params(filter) });
  }

  request(id: number): Observable<RequestDetailDto> {
    return this.http.get<RequestDetailDto>(`/api/requests/${id}`);
  }

  createRequest(body: {
    title: string; description: string; type: RequestType; requestedUrgency: RequestedUrgency;
    businessImpact?: string; expectedResult?: string; currentResult?: string;
    reproductionSteps?: string; targetDate?: string | null;
  }): Observable<RequestDetailDto> {
    return this.http.post<RequestDetailDto>('/api/requests', body);
  }

  updateRequest(id: number, body: Record<string, unknown>): Observable<RequestDetailDto> {
    return this.http.put<RequestDetailDto>(`/api/requests/${id}`, body);
  }

  reviewQueue(page = 1, pageSize = 25): Observable<PagedResult<RequestSummaryDto>> {
    return this.http.get<PagedResult<RequestSummaryDto>>('/api/requests/review-queue', {
      params: params({ page, pageSize }),
    });
  }

  startReview(id: number): Observable<RequestDetailDto> {
    return this.http.post<RequestDetailDto>(`/api/requests/${id}/start-review`, {});
  }

  triage(id: number, body: {
    outcome: TriageOutcome; reason?: string; approvedPriority?: Priority;
    estimatedEffortHours?: number; dueDate?: string | null; acceptanceCriteria?: string;
    duplicateOfRequestId?: number;
  }): Observable<RequestDetailDto> {
    return this.http.post<RequestDetailDto>(`/api/requests/${id}/triage`, body);
  }

  answerClarification(clarificationId: number, answer: string): Observable<RequestDetailDto> {
    return this.http.post<RequestDetailDto>(
      `/api/requests/clarifications/${clarificationId}/answer`, { answer });
  }

  uploadRequestAttachment(requestId: number, file: File): Observable<AttachmentDto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<AttachmentDto>(`/api/requests/${requestId}/attachments`, form);
  }

  uploadTaskAttachment(taskId: number, file: File): Observable<AttachmentDto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<AttachmentDto>(`/api/tasks/${taskId}/attachments`, form);
  }

  downloadAttachment(id: number): Observable<Blob> {
    return this.http.get(`/api/attachments/${id}`, { responseType: 'blob' });
  }

  // --- tasks ---------------------------------------------------------------------------------

  tasks(filter: {
    status?: WorkTaskStatus; priority?: Priority; assigneeUserId?: number; unassigned?: boolean;
    openOnly?: boolean; search?: string; page?: number; pageSize?: number;
  }): Observable<PagedResult<TaskSummaryDto>> {
    return this.http.get<PagedResult<TaskSummaryDto>>('/api/tasks', { params: params(filter) });
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

  pauseReasons(): Observable<PauseReasonDto[]> {
    return this.http.get<PauseReasonDto[]>('/api/tasks/pause-reasons');
  }

  activeSession(): Observable<WorkSessionDto | null> {
    return this.http.get<WorkSessionDto | null>('/api/tasks/active-session');
  }

  transition(id: number, body: {
    to: WorkTaskStatus; reason?: string; isOverride?: boolean; idempotencyKey?: string;
  }): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/transition`, body);
  }

  assign(id: number, assigneeUserId: number | null, reason?: string, rowVersion?: string | null):
    Observable<TaskDetailDto> {
    return this.http.put<TaskDetailDto>(`/api/tasks/${id}/assignee`, {
      assigneeUserId, reason, rowVersion,
    });
  }

  setTaskRoles(id: number, reviewerUserId: number | null, qcUserId: number | null):
    Observable<TaskDetailDto> {
    return this.http.put<TaskDetailDto>(`/api/tasks/${id}/roles`, { reviewerUserId, qcUserId });
  }

  addCollaborator(id: number, userId: number): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/collaborators`, { userId });
  }

  removeCollaborator(id: number, userId: number): Observable<TaskDetailDto> {
    return this.http.delete<TaskDetailDto>(`/api/tasks/${id}/collaborators/${userId}`);
  }

  updateTaskDetails(id: number, body: {
    priority?: Priority; estimatedEffortHours?: number; dueDate?: string | null;
    acceptanceCriteria?: string; resolution?: string; progressPercent?: number;
  }): Observable<TaskDetailDto> {
    return this.http.patch<TaskDetailDto>(`/api/tasks/${id}`, body);
  }

  // --- the timer -----------------------------------------------------------------------------

  startWork(id: number): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/start`, {});
  }

  pauseWork(id: number, pauseReasonId?: number, comment?: string): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/pause`, { pauseReasonId, comment });
  }

  blockWork(id: number, pauseReasonId?: number, comment?: string): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/block`, { pauseReasonId, comment });
  }

  completeWork(id: number, resolution?: string): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/complete`, { resolution });
  }

  interrupt(taskId: number, reason?: string): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>('/api/tasks/interrupt', { taskId, reason });
  }

  // --- QC & closure --------------------------------------------------------------------------

  startQC(id: number): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/qc/start`, {});
  }

  submitQC(id: number, body: {
    result: QCResult; comments?: string; environment?: string; buildVersion?: string;
    criteria: { index: number; met: boolean; note?: string }[];
  }): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/qc/review`, body);
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

  closeTask(id: number, resolution?: string, reason?: string): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/close`, { resolution, reason });
  }

  reopenTask(id: number, reason: string): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/reopen`, { reason });
  }

  // --- collaboration -------------------------------------------------------------------------

  comments(id: number): Observable<TaskCommentDto[]> {
    return this.http.get<TaskCommentDto[]>(`/api/tasks/${id}/comments`);
  }

  addComment(id: number, body: {
    body: string; category: CommentCategory; visibleToRequester?: boolean | null;
  }): Observable<TaskCommentDto> {
    return this.http.post<TaskCommentDto>(`/api/tasks/${id}/comments`, body);
  }

  dependencies(id: number): Observable<TaskDependencyGraphDto> {
    return this.http.get<TaskDependencyGraphDto>(`/api/tasks/${id}/dependencies`);
  }

  addDependency(id: number, relatedTaskId: number, type: DependencyType):
    Observable<TaskDependencyGraphDto> {
    return this.http.post<TaskDependencyGraphDto>(`/api/tasks/${id}/dependencies`, {
      relatedTaskId, type,
    });
  }

  removeDependency(id: number, dependencyId: number): Observable<TaskDependencyGraphDto> {
    return this.http.delete<TaskDependencyGraphDto>(`/api/tasks/${id}/dependencies/${dependencyId}`);
  }

  subtasks(id: number): Observable<PagedResult<TaskSummaryDto>> {
    return this.http.get<PagedResult<TaskSummaryDto>>(`/api/tasks/${id}/subtasks`);
  }

  createSubtask(id: number, body: {
    title: string; description: string; priority?: Priority; estimatedEffortHours?: number;
    dueDate?: string | null; acceptanceCriteria?: string; assigneeUserId?: number | null;
  }): Observable<TaskDetailDto> {
    return this.http.post<TaskDetailDto>(`/api/tasks/${id}/subtasks`, body);
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

  approveScopeChange(scopeChangeId: number): Observable<ScopeChangeDto> {
    return this.http.post<ScopeChangeDto>(`/api/tasks/scope-changes/${scopeChangeId}/approve`, {});
  }

  // --- shifts & workforce ----------------------------------------------------------------------

  myShiftStatus(): Observable<WorkforceStatusDto> {
    return this.http.get<WorkforceStatusDto>('/api/shifts/current');
  }

  startShift(): Observable<WorkforceStatusDto> {
    return this.http.post<WorkforceStatusDto>('/api/shifts/start', {});
  }

  endShift(note?: string): Observable<WorkforceStatusDto> {
    return this.http.post<WorkforceStatusDto>('/api/shifts/end', { note });
  }

  setWorkforceState(state: WorkforceState, note?: string): Observable<WorkforceStatusDto> {
    return this.http.put<WorkforceStatusDto>('/api/shifts/state', { state, note });
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

  forceEndShift(userId: number, reason: string): Observable<unknown> {
    return this.http.post(`/api/workforce/${userId}/end-shift`, { reason });
  }

  // --- dashboards & reports ----------------------------------------------------------------------

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
}
