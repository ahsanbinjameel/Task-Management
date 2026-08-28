import { Perm } from './permissions';

/**
 * The sidebar, as the user's job rather than as the database's shape (PRODUCT-CORE §11).
 *
 * The rail used to carry sixteen links in six sections — Review queue, Assignment queue, QC queue,
 * Workload, Who's working, Audit log and three Admin screens each had their own entry, which is a
 * table of contents for the schema, not a description of anybody's day. Nobody's job is "assignment
 * queue".
 *
 * So a group is a job, and the destinations underneath it are *views inside* that job. Requests
 * holds the review queue; Tasks holds the assignment queue; Quality holds the QC queue and the
 * standalone Checks; Team holds workload, who's working and the reports. The routes and their
 * guards are untouched — this decides what is *offered*, and a deep link still lands exactly as it
 * did before.
 *
 * Administration is deliberately absent: it lives behind Settings, which is already the one door
 * out of the profile menu and already links to users, roles, setup data and the audit log.
 */
export interface NavView {
  label: string;
  route: string;
  /** Reachable when the user holds any of these. Undefined means everyone. */
  permissions?: string[];
}

export interface NavGroup {
  /** Stable id, used by `app-view-tabs` to find its own group. */
  key: string;
  label: string;
  icon: string;
  /**
   * The views inside this group, in preference order. The first one the reader can actually reach
   * becomes the sidebar link; every one of them lights the group up while you are on it, because
   * they are views of one job rather than destinations of their own.
   */
  views: NavView[];
}

/**
 * Note which permissions are deliberately *not* here.
 *
 * `Task.Work` and `Task.QCReview` no longer open the Tasks group. A worker navigates by their own
 * queue and a checker by the QC queue; browsing every task in the system is a coordinating act, and
 * putting it in front of the other two is how the rail grew. Both still hold the permission and the
 * route guard still admits them, so a link, a bookmark or a search result works — it is simply not
 * offered as part of their job.
 */
export const NAV_GROUPS: NavGroup[] = [
  {
    key: 'home',
    label: 'Home',
    icon: 'home',
    views: [{ label: 'Home', route: '/' }],
  },
  {
    key: 'my-tasks',
    label: 'My tasks',
    icon: 'checklist',
    views: [
      { label: 'My queue', route: '/my-queue', permissions: [Perm.taskWork] },
      { label: 'My day', route: '/me/day', permissions: [Perm.workforceTrackShift] },
    ],
  },
  {
    key: 'requests',
    label: 'Requests',
    icon: 'inbox',
    views: [
      {
        label: 'All requests',
        route: '/requests',
        permissions: [Perm.requestCreate, Perm.requestViewAll, Perm.taskReview],
      },
      { label: 'To review', route: '/review-queue', permissions: [Perm.taskReview] },
    ],
  },
  {
    key: 'tasks',
    label: 'Tasks',
    icon: 'task_alt',
    views: [
      {
        label: 'All tasks',
        route: '/tasks',
        permissions: [
          Perm.taskAssign, Perm.taskReview, Perm.requestViewAll, Perm.dashboardManagement,
        ],
      },
      { label: 'To assign', route: '/assignment', permissions: [Perm.taskAssign] },
    ],
  },
  {
    key: 'quality',
    label: 'Quality',
    icon: 'verified',
    views: [
      { label: 'QC queue', route: '/qc', permissions: [Perm.taskQCReview] },
      {
        label: 'Checks',
        route: '/verifications',
        permissions: [Perm.verificationCreate, Perm.verificationWork, Perm.verificationViewAll],
      },
    ],
  },
  {
    key: 'team',
    label: 'Team',
    icon: 'groups',
    views: [
      { label: 'Workload', route: '/workload', permissions: [Perm.workforceViewAll] },
      { label: "Who's working", route: '/workforce', permissions: [Perm.workforceViewAll] },
      { label: 'Reports', route: '/reports', permissions: [Perm.reportsView] },
    ],
  },
];

export function navGroup(key: string): NavGroup | undefined {
  return NAV_GROUPS.find((g) => g.key === key);
}
