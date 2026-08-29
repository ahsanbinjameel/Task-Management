/**
 * Mirrors `WorkflowApp.Application.Common.Permissions`. Kept as constants so a typo is a compile
 * error rather than a menu item that silently never appears.
 *
 * These drive what the UI *offers*. They are not security — every one of them is enforced again
 * server-side, and hiding a button has never stopped anyone calling the endpoint.
 */
export const Perm = {
  requestCreate: 'Request.Create',
  requestViewOwn: 'Request.ViewOwn',
  requestViewAll: 'Request.ViewAll',

  taskReview: 'Task.Review',
  taskApprove: 'Task.Approve',
  taskAssign: 'Task.Assign',
  taskWork: 'Task.Work',
  taskQCReview: 'Task.QCReview',
  taskClose: 'Task.Close',
  taskReopen: 'Task.Reopen',
  taskCancel: 'Task.Cancel',
  taskDefer: 'Task.Defer',
  taskOverride: 'Task.Override',

  verificationCreate: 'Verification.Create',
  verificationWork: 'Verification.Work',
  verificationViewAll: 'Verification.ViewAll',

  workforceViewAll: 'Workforce.ViewAll',
  workforceManageOthers: 'Workforce.ManageOthers',
  workforceTrackShift: 'Workforce.TrackShift',

  dashboardManagement: 'Dashboard.Management',
  reportsView: 'Reports.View',

  adminManageUsers: 'Admin.ManageUsers',
  adminManageRoles: 'Admin.ManageRoles',
  adminManageConfig: 'Admin.ManageConfig',
  adminViewAudit: 'Admin.ViewAudit',
  adminDemoMode: 'Admin.DemoMode',
} as const;

export type PermissionKey = (typeof Perm)[keyof typeof Perm];
