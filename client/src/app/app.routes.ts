import { Routes } from '@angular/router';
import { authGuard, requirePermission } from './core/guards';
import { Perm } from './core/permissions';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    children: [
      {
        path: '',
        title: 'Dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },

      // --- work ---------------------------------------------------------------------------
      {
        path: 'my-queue',
        title: 'My queue',
        canActivate: [requirePermission(Perm.taskWork)],
        loadComponent: () =>
          import('./features/tasks/my-queue.component').then((m) => m.MyQueueComponent),
      },
      {
        path: 'tasks',
        title: 'Tasks',
        // Mirrors the menu: hiding the link is a courtesy, this is what makes the URL agree.
        canActivate: [requirePermission(
          Perm.taskWork, Perm.taskAssign, Perm.taskReview, Perm.taskQCReview,
          Perm.requestViewAll, Perm.dashboardManagement)],
        loadComponent: () =>
          import('./features/tasks/task-list.component').then((m) => m.TaskListComponent),
      },
      {
        path: 'tasks/:id',
        title: 'Task',
        loadComponent: () =>
          import('./features/tasks/task-detail.component').then((m) => m.TaskDetailComponent),
      },

      // --- intake -------------------------------------------------------------------------
      {
        path: 'requests',
        title: 'Requests',
        canActivate: [requirePermission(
          Perm.requestCreate, Perm.requestViewAll, Perm.taskReview)],
        loadComponent: () =>
          import('./features/requests/request-list.component').then((m) => m.RequestListComponent),
      },
      {
        path: 'requests/new',
        title: 'New request',
        canActivate: [requirePermission(Perm.requestCreate)],
        loadComponent: () =>
          import('./features/requests/request-create.component').then((m) => m.RequestCreateComponent),
      },
      // Ahead of `requests/:id` on purpose: `batches` is not an id, and a route table is matched
      // in order.
      {
        path: 'requests/batches/:id',
        title: 'Submission',
        loadComponent: () =>
          import('./features/requests/batch-detail.component').then((m) => m.BatchDetailComponent),
      },
      {
        path: 'requests/:id',
        title: 'Request',
        loadComponent: () =>
          import('./features/requests/request-detail.component').then((m) => m.RequestDetailComponent),
      },
      {
        path: 'review-queue',
        title: 'Review queue',
        canActivate: [requirePermission(Perm.taskReview)],
        loadComponent: () =>
          import('./features/requests/review-queue.component').then((m) => m.ReviewQueueComponent),
      },

      // --- coordinate ---------------------------------------------------------------------
      {
        path: 'assignment',
        title: 'Assignment queue',
        canActivate: [requirePermission(Perm.taskAssign)],
        loadComponent: () =>
          import('./features/tasks/assignment-queue.component').then((m) => m.AssignmentQueueComponent),
      },
      {
        path: 'workload',
        title: 'Workload',
        canActivate: [requirePermission(Perm.workforceViewAll)],
        loadComponent: () =>
          import('./features/workforce/workload.component').then((m) => m.WorkloadComponent),
      },
      {
        path: 'workforce',
        title: "Who's working",
        canActivate: [requirePermission(Perm.workforceViewAll)],
        loadComponent: () =>
          import('./features/workforce/active-workforce.component').then((m) => m.ActiveWorkforceComponent),
      },

      // --- quality ------------------------------------------------------------------------
      {
        path: 'qc',
        title: 'QC queue',
        canActivate: [requirePermission(Perm.taskQCReview)],
        loadComponent: () =>
          import('./features/qc/qc-queue.component').then((m) => m.QcQueueComponent),
      },

      // --- insight ------------------------------------------------------------------------
      {
        path: 'reports',
        title: 'Reports',
        canActivate: [requirePermission(Perm.reportsView)],
        loadComponent: () =>
          import('./features/reports/reports.component').then((m) => m.ReportsComponent),
      },
      {
        path: 'audit',
        title: 'Audit log',
        canActivate: [requirePermission(Perm.adminViewAudit)],
        loadComponent: () =>
          import('./features/admin/audit.component').then((m) => m.AuditComponent),
      },

      // --- admin --------------------------------------------------------------------------
      {
        path: 'admin/users',
        title: 'Users',
        canActivate: [requirePermission(Perm.adminManageUsers)],
        loadComponent: () =>
          import('./features/admin/users.component').then((m) => m.UsersComponent),
      },
      {
        path: 'admin/roles',
        title: 'Roles',
        canActivate: [requirePermission(Perm.adminManageRoles)],
        loadComponent: () =>
          import('./features/admin/roles.component').then((m) => m.RolesComponent),
      },

      // --- me -----------------------------------------------------------------------------
      {
        path: 'me/day',
        title: 'My day',
        // Shift and timer tooling belongs to people on the clock. Hiding the menu item is not
        // enough — without this the page is still reachable by typing the URL.
        canActivate: [requirePermission(Perm.workforceTrackShift)],
        loadComponent: () =>
          import('./features/me/my-day.component').then((m) => m.MyDayComponent),
      },
      {
        path: 'admin/setup',
        title: 'Setup data',
        canActivate: [requirePermission(Perm.adminManageConfig)],
        loadComponent: () =>
          import('./features/admin/setup.component').then((m) => m.SetupComponent),
      },
      {
        path: 'me/settings',
        title: 'Settings',
        loadComponent: () =>
          import('./features/me/settings.component').then((m) => m.SettingsComponent),
      },
      // The change-password screen was folded into Settings. Kept as a redirect rather than
      // deleted: it was a menu item for long enough to be bookmarked, and a dead link inside your
      // own app is a worse answer than sending someone to where the thing actually went.
      { path: 'me/password', redirectTo: 'me/settings', pathMatch: 'full' },
    ],
  },
  { path: '**', redirectTo: '' },
];
