import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpContext } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { map } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { FormSubmit } from '../../core/form-submit';
import { Perm } from '../../core/permissions';
import { RoleDetailDto } from '../../core/models';
import { roleLabel } from '../../core/labels';
import { ConfirmDialog, ConfirmData } from '../../shared/dialogs';
import { LoadingComponent, PageHeaderComponent } from '../../shared/ui';

/**
 * The permission catalogue, grouped by the thing it acts on.
 *
 * Grouping is derived from the key's own prefix rather than from a hand-kept list, so a permission
 * added server-side shows up here without a second edit — which is the only way two catalogues stay
 * in step. The headings are the wording layer; the keys themselves are shown because an
 * administrator granting rights needs to see exactly what is being granted.
 */
const GROUP_LABELS: Record<string, string> = {
  Request: 'Requests',
  Task: 'Tasks and workflow',
  Workforce: 'Shifts and availability',
  Dashboard: 'Dashboards',
  Reports: 'Reports',
  Admin: 'Administration',
};

interface PermissionGroup {
  label: string;
  keys: string[];
}

/** Name and description. Kept apart from the permission grid, which is a different job. */
@Component({
  selector: 'app-role-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.role ? 'Rename this role' : 'Add a role' }}</h2>
    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      @if (data.role?.isSystemRole) {
        <p class="locked">
          <mat-icon>lock</mat-icon>
          <span>
            Built-in roles cannot be renamed — the seeder recreates them by name, so a rename would
            produce a second copy on the next restart. You can still change the description, and
            what the role grants.
          </span>
        </p>
      }

      <mat-form-field class="full">
        <mat-label>Name</mat-label>
        <input matInput name="name" [(ngModel)]="name" cdkFocusInitial maxlength="100"
               [disabled]="!!data.role?.isSystemRole" />
        @if (form.fieldError('name'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>What it is for (optional)</mat-label>
        <textarea matInput rows="2" name="description" [(ngModel)]="description"
                  maxlength="500"></textarea>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!name.trim() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving…' : 'Save' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    mat-dialog-content { min-width: min(460px, 84vw); padding-top: 8px !important; }
    .locked {
      display: flex; gap: 8px; align-items: flex-start; margin: 0 0 14px;
      padding: 10px 12px; border-radius: 8px; font-size: 13px; line-height: 1.45;
      background: var(--tone-warn-bg); color: var(--tone-warn-fg);
    }
    .locked mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class RoleDialog {
  readonly data = inject<{ role: RoleDetailDto | null }>(MAT_DIALOG_DATA);
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<RoleDialog>);
  readonly form = new FormSubmit();

  name = this.data.role?.name ?? '';
  description = this.data.role?.description ?? '';

  save(): void {
    const body = { name: this.name.trim(), description: this.description.trim() || null };

    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.data.role
        ? this.api.updateRole(this.data.role.id, body, ctx)
        : this.api.createRole(body, ctx),
      (saved) => { this.ref.disableClose = false; this.ref.close(saved ?? true); },
    );
  }
}

/**
 * Roles are bundles of permissions, nothing more — every check server-side is on a permission,
 * never a role name. This screen is both the readable map of who can do what and, for anyone
 * holding `Admin.ManageRoles`, the place to change it.
 *
 * Editing is per-role and explicit: tick what the role grants, then save. Nothing is applied on a
 * stray click, because the blast radius is everyone holding the role — and it does not take effect
 * until they next sign in, since permissions travel on the access token. The screen says so rather
 * than leaving an administrator wondering why nothing changed.
 *
 * The server refuses two things this UI does not try to hide: renaming a built-in role, and
 * removing the last held route to role management. Both come back as ordinary messages.
 */
@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [
    MatExpansionModule, MatIconModule, MatButtonModule, MatCheckboxModule, MatTooltipModule,
    PageHeaderComponent, LoadingComponent,
  ],
  template: `
    <div class="page narrow">
      <app-page-header title="Roles">
        @if (canEdit) {
          <button matButton="filled" (click)="addRole()">
            <mat-icon>add</mat-icon> Add a role
          </button>
        }
      </app-page-header>

      @if (canEdit) {
        <p class="muted small hint">
          A change takes effect the next time the person signs in — permissions travel on the
          access token.
        </p>
      }

      @if (loading()) {
        <app-loading />
      } @else {
        <mat-accordion multi>
          @for (role of roles(); track role.id) {
            <mat-expansion-panel>
              <mat-expansion-panel-header>
                <mat-panel-title>
                  <strong>{{ roleLabel(role.name) }}</strong>
                  @if (role.isSystemRole) { <span class="chip tone-muted sys">Built in</span> }
                </mat-panel-title>
                <mat-panel-description>
                  {{ role.permissions.length }} permissions ·
                  {{ role.userCount === 0 ? 'nobody holds it' : role.userCount + ' holding it' }}
                </mat-panel-description>
              </mat-expansion-panel-header>

              @if (role.description) { <p class="muted small">{{ role.description }}</p> }

              @if (canEdit) {
                <div class="groups">
                  @for (group of groups(); track group.label) {
                    <div class="group">
                      <h3>{{ group.label }}</h3>
                      @for (key of group.keys; track key) {
                        <mat-checkbox [checked]="isOn(role.id, key)"
                                      (change)="toggle(role.id, key, $event.checked)">
                          <span class="mono small">{{ key }}</span>
                        </mat-checkbox>
                      }
                    </div>
                  }
                </div>

                <div class="row actions">
                  <button matButton (click)="editRole(role)">
                    <mat-icon>edit</mat-icon> Name and description
                  </button>
                  @if (!role.isSystemRole) {
                    <button matButton (click)="removeRole(role)"
                            [matTooltip]="role.userCount > 0
                              ? 'Someone holds this role — take it off them first'
                              : 'Delete this role'">
                      <mat-icon>delete_outline</mat-icon> Delete
                    </button>
                  }
                  <span class="spacer"></span>
                  @if (dirty(role.id)) {
                    <button matButton (click)="reset(role)">Undo</button>
                  }
                  <button matButton="filled" [disabled]="!dirty(role.id) || saving()"
                          (click)="savePermissions(role)">
                    {{ saving() ? 'Saving…' : 'Save permissions' }}
                  </button>
                </div>
              } @else {
                <div class="perms">
                  @for (permission of role.permissions; track permission) {
                    <span class="chip tone-neutral mono">{{ permission }}</span>
                  } @empty {
                    <span class="muted small">No permissions granted.</span>
                  }
                </div>
              }
            </mat-expansion-panel>
          }
        </mat-accordion>
      }
    </div>
  `,
  styles: `
    .narrow { max-width: 900px; }
    .sys { margin-left: 9px; }
    .hint { margin: 0 0 12px; }
    .perms { display: flex; flex-wrap: wrap; gap: 6px; }
    .groups { display: grid; gap: 16px; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); }
    .group { display: flex; flex-direction: column; gap: 4px; }
    .group h3 {
      margin: 0 0 2px; font-size: 12px; text-transform: uppercase;
      letter-spacing: .04em; color: var(--text-muted);
    }
    .actions { margin-top: 18px; gap: 8px; flex-wrap: wrap; }
  `,
})
export class RolesComponent implements OnInit {
  /** The wording layer — `AssignmentManager` is not a word anyone says. */
  readonly roleLabel = roleLabel;

  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  readonly canEdit = this.auth.has(Perm.adminManageRoles);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly roles = signal<RoleDetailDto[]>([]);
  readonly groups = signal<PermissionGroup[]>([]);

  /** Ticked state per role while it is being edited, keyed by role id. */
  private readonly draft = signal<Record<number, Set<string>>>({});

  ngOnInit(): void {
    this.load();

    // The catalogue, not the role's own list: an administrator has to be able to grant something
    // the role does not have yet.
    this.api.permissionCatalog().subscribe({
      next: (keys) => this.groups.set(this.group(keys)),
      error: () => this.groups.set([]),
    });
  }

  private load(): void {
    this.loading.set(true);

    // Someone who may read roles but not change them uses the older, narrower endpoint — it is
    // gated on a different permission and carries no holder count, so that column reads as zero and
    // is never shown to them anyway.
    const source = this.canEdit
      ? this.api.setupRoles()
      : this.api.roles().pipe(
          map((roles) => roles.map((r): RoleDetailDto => ({ ...r, userCount: 0 }))));

    source.subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.draft.set(Object.fromEntries(roles.map((r) => [r.id, new Set(r.permissions)])));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private group(keys: string[]): PermissionGroup[] {
    const buckets = new Map<string, string[]>();

    for (const key of [...keys].sort()) {
      const prefix = key.split('.')[0];
      buckets.set(prefix, [...(buckets.get(prefix) ?? []), key]);
    }

    return [...buckets.entries()].map(([prefix, groupKeys]) => ({
      label: GROUP_LABELS[prefix] ?? prefix,
      keys: groupKeys,
    }));
  }

  // --- the draft ---------------------------------------------------------------------------

  isOn = (roleId: number, key: string): boolean =>
    this.draft()[roleId]?.has(key) ?? false;

  toggle(roleId: number, key: string, on: boolean): void {
    this.draft.update((all) => {
      const next = new Set(all[roleId] ?? []);
      if (on) next.add(key); else next.delete(key);
      return { ...all, [roleId]: next };
    });
  }

  dirty(roleId: number): boolean {
    const role = this.roles().find((r) => r.id === roleId);
    const draft = this.draft()[roleId];
    if (!role || !draft) return false;

    return role.permissions.length !== draft.size
      || role.permissions.some((p) => !draft.has(p));
  }

  reset(role: RoleDetailDto): void {
    this.draft.update((all) => ({ ...all, [role.id]: new Set(role.permissions) }));
  }

  // --- writes ------------------------------------------------------------------------------

  savePermissions(role: RoleDetailDto): void {
    const permissions = [...(this.draft()[role.id] ?? [])];
    this.saving.set(true);

    this.api.setRolePermissions(role.id, permissions).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success(
          `${roleLabel(role.name)} updated. It applies at their next sign-in.`);
        this.load();
      },
      // The server refuses the last route to role management; the message says why.
      error: () => { this.saving.set(false); this.load(); },
    });
  }

  addRole(): void {
    this.dialog.open(RoleDialog, { data: { role: null }, width: 'min(500px, 92vw)' })
      .afterClosed().subscribe((saved) => {
        if (!saved) return;
        this.toast.success('Role added. Tick what it grants, then save.');
        this.load();
      });
  }

  editRole(role: RoleDetailDto): void {
    this.dialog.open(RoleDialog, { data: { role }, width: 'min(500px, 92vw)' })
      .afterClosed().subscribe((saved) => {
        if (!saved) return;
        this.toast.success('Saved.');
        this.load();
      });
  }

  removeRole(role: RoleDetailDto): void {
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: `Delete ${roleLabel(role.name)}?`,
          message: role.userCount > 0
            ? `${role.userCount} ${role.userCount === 1 ? 'person holds' : 'people hold'} this role, `
              + 'so it cannot be deleted — take it off them first.'
            : 'Nobody holds this role, so nothing changes for anyone. This cannot be undone.',
          confirmText: 'Delete it',
          danger: true,
          submit: (ctx: HttpContext) => this.api.deleteRole(role.id, ctx),
        },
      })
      .afterClosed()
      .subscribe((done?: unknown) => {
        if (!done) return;
        this.toast.success('Role deleted.');
        this.load();
      });
  }
}
