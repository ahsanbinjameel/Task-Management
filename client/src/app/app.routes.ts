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
        loadComponent: () =>
          import('./features/me/my-day.component').then((m) => m.MyDayComponent),
      },
      {
        path: 'me/password',
        title: 'Change password',
        loadComponent: () =>
          import('./features/me/change-password.component').then((m) => m.ChangePasswordComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
