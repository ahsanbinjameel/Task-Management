import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import {
  MatDialog,
  MatDialogModule,
  MatDialogRef,
  MAT_DIALOG_DATA,
} from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { ApiService } from '../../core/api.service';
import { SubmitFailure, describeFailure, handledLocally } from '../../core/form-errors';
import { ToastService } from '../../core/toast.service';
import { PagedResult, RoleDto, UserDto } from '../../core/models';
import { SearchSelectComponent } from '../../shared/search-select.component';
import {
  ChipComponent,
  EmptyComponent,
  LoadingComponent,
  PageHeaderComponent,
} from '../../shared/ui';
import { ReasonDialog, ReasonData } from '../../shared/dialogs';

@Component({
  selector: 'app-user-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>Add a person</h2>
    <mat-dialog-content>
      @if (failure(); as f) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon>
          <span>{{ f.message }}</span>
        </div>
      }

      <mat-form-field class="full">
        <mat-label>Username or employee code</mat-label>
        <input
          matInput
          name="username"
          [(ngModel)]="userName"
          (ngModelChange)="clearField('username')"
        />
        @if (fieldError('username'); as e) {
          <mat-error>{{ e }}</mat-error>
        }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Full name</mat-label>
        <input
          matInput
          name="displayname"
          [(ngModel)]="displayName"
          (ngModelChange)="clearField('displayname')"
        />
        @if (fieldError('displayname'); as e) {
          <mat-error>{{ e }}</mat-error>
        }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Email (optional)</mat-label>
        <input
          matInput
          type="email"
          name="email"
          [(ngModel)]="email"
          (ngModelChange)="clearField('email')"
        />
        @if (fieldError('email'); as e) {
          <mat-error>{{ e }}</mat-error>
        }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Temporary password</mat-label>
        <input
          matInput
          name="password"
          [(ngModel)]="password"
          (ngModelChange)="clearField('password')"
        />
        @if (fieldError('password'); as e) {
          <mat-error>{{ e }}</mat-error>
        }
      </mat-form-field>

      <app-search-select class="full" label="What they can do" multiple name="roles"
                         [options]="roleOptions" [(ngModel)]="roles" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="busy()">Cancel</button>
      <button matButton="filled" [disabled]="!ready() || busy()" (click)="submit()">
        {{ busy() ? 'Saving...' : 'Add person' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full {
      width: 100%;
    }
    mat-dialog-content {
      min-width: min(440px, 82vw);
      padding-top: 8px !important;
    }
    /* The failure is reported inside the form, next to the work it refers to. */
    .form-error {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      margin: 0 0 14px;
      padding: 10px 12px;
      border-radius: 8px;
      font-size: 13.5px;
      line-height: 1.45;
      background: var(--tone-danger-bg);
      color: var(--tone-danger-fg);
    }
    .form-error mat-icon {
      font-size: 18px;
      width: 18px;
      height: 18px;
      flex: none;
      margin-top: 1px;
    }
  `,
})
/**
 * The dialog owns its submit.
 *
 * It used to close immediately and hand the values back for the caller to POST. When the server
 * rejected the password the modal was already gone, everything typed was lost, and the reason
 * surfaced as a toast floating outside the form. Now the request happens here: on failure the
 * dialog stays open with every value intact and the message rendered against the field it belongs
 * to; it closes only on success, or when the person cancels.
 */
export class UserDialog {
  readonly ref = inject(MatDialogRef<UserDialog>);
  readonly data = inject<{ roles: RoleDto[] }>(MAT_DIALOG_DATA);
  private readonly api = inject(ApiService);

  userName = '';
  displayName = '';
  email = '';
  password = '';
  roles: string[] = [];
  readonly roleOptions = this.data.roles.map((role) => ({ value: role.name, label: role.name }));

  readonly busy = signal(false);
  readonly failure = signal<SubmitFailure | null>(null);

  constructor() {
    // Nothing is discarded behind the user's back: closing is their decision, or a success.
    this.ref.disableClose = true;
  }

  /** Only the genuinely required fields. Email is deliberately not one of them. */
  ready = () =>
    this.userName.trim().length > 0 &&
    this.displayName.trim().length > 0 &&
    this.password.length > 0;

  fieldError = (name: string): string | null => this.failure()?.fields[name] ?? null;

  /** Clear a field's error as soon as it is edited, so the form stops nagging about it. */
  clearField(name: string): void {
    const current = this.failure();
    if (!current?.fields[name]) return;

    const remaining = { ...current.fields };
    delete remaining[name];
    this.failure.set({ ...current, fields: remaining });
  }

  submit(): void {
    if (!this.ready() || this.busy()) return;

    this.busy.set(true);
    this.failure.set(null);

    this.api
      .createUser(
        {
          userName: this.userName.trim(),
          displayName: this.displayName.trim(),
          email: this.email.trim() || undefined,
          password: this.password,
          roles: this.roles,
        },
        handledLocally(),
      )
      .subscribe({
        next: (user) => {
          this.busy.set(false);
          this.ref.close(user);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.failure.set(describeFailure(error));
          this.focusFirstInvalid();
        },
      });
  }

  private focusFirstInvalid(): void {
    const first = Object.keys(this.failure()?.fields ?? {})[0];
    if (!first) return;

    queueMicrotask(() => {
      document.querySelector<HTMLElement>('[name="' + first + '"]')?.focus();
    });
  }
}

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatSlideToggleModule,
    MatTableModule,
    PageHeaderComponent,
    ChipComponent,
    EmptyComponent,
    LoadingComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Users" subtitle="Accounts, roles and access.">
        <button matButton="filled" (click)="create()">
          <mat-icon>person_add</mat-icon> New user
        </button>
      </app-page-header>

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (users().length === 0) {
          <app-empty message="No users" icon="group" />
        } @else {
          <div class="table-scroll">
            <table mat-table [dataSource]="users()">
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>Name</th>
                <td mat-cell *matCellDef="let u">
                  <strong>{{ u.displayName }}</strong>
                  <div class="muted small">
                    {{ u.userName }}{{ u.email ? ' · ' + u.email : '' }}
                  </div>
                </td>
              </ng-container>

              <ng-container matColumnDef="roles">
                <th mat-header-cell *matHeaderCellDef>Roles</th>
                <td mat-cell *matCellDef="let u">
                  <div class="row row-wrap" style="gap:5px">
                    @for (role of u.roles; track role) {
                      <span class="chip tone-neutral">{{ role }}</span>
                    } @empty {
                      <span class="muted small">None</span>
                    }
                  </div>
                </td>
              </ng-container>

              <ng-container matColumnDef="state">
                <th mat-header-cell *matHeaderCellDef>Availability</th>
                <td mat-cell *matCellDef="let u">
                  <app-chip [value]="u.workforceState" kind="workforce" [dot]="true" />
                </td>
              </ng-container>

              <ng-container matColumnDef="active">
                <th mat-header-cell *matHeaderCellDef>Active</th>
                <td mat-cell *matCellDef="let u">
                  <mat-slide-toggle
                    [checked]="u.isActive"
                    (change)="setActive(u, $event.checked)"
                  />
                </td>
              </ng-container>

              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef></th>
                <td mat-cell *matCellDef="let u" class="right">
                  <button matIconButton [matMenuTriggerFor]="menu">
                    <mat-icon>more_vert</mat-icon>
                  </button>
                  <mat-menu #menu="matMenu">
                    <button mat-menu-item (click)="editRoles(u)">
                      <mat-icon>badge</mat-icon><span>Change roles</span>
                    </button>
                    <button mat-menu-item (click)="resetPassword(u)">
                      <mat-icon>lock_reset</mat-icon><span>Reset password</span>
                    </button>
                  </mat-menu>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns"></tr>
            </table>
          </div>
        }
      </div>

      @if (editing(); as u) {
        <div class="card card-pad top-gap">
          <h2 class="card-title">Roles for {{ u.displayName }}</h2>
          <p class="muted small">
            A permission change only takes effect the next time they sign in — permissions travel on
            the access token.
          </p>
          <div class="roles">
            @for (role of roles(); track role.id) {
              <mat-checkbox
                [checked]="selectedRoles.includes(role.name)"
                (change)="toggleRole(role.name, $event.checked)"
              >
                {{ role.name }}
                <span class="muted small"> — {{ role.permissions.length }} permissions</span>
              </mat-checkbox>
            }
          </div>
          <div class="row top-gap">
            <span class="spacer"></span>
            <button matButton (click)="editing.set(null)">Cancel</button>
            <button matButton="filled" (click)="saveRoles(u)">Save roles</button>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    .right {
      text-align: right;
    }
    .top-gap {
      margin-top: 18px;
    }
    .roles {
      display: grid;
      gap: 6px;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
    }
  `,
})
export class UsersComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  readonly users = signal<UserDto[]>([]);
  readonly roles = signal<RoleDto[]>([]);
  readonly loading = signal(true);
  readonly editing = signal<UserDto | null>(null);

  selectedRoles: string[] = [];
  readonly columns = ['name', 'roles', 'state', 'active', 'actions'];

  ngOnInit(): void {
    this.load();
    this.api.roles().subscribe({ next: (r) => this.roles.set(r), error: () => undefined });
  }

  load(): void {
    this.api.users({ pageSize: 200 }).subscribe({
      next: (page: PagedResult<UserDto>) => {
        this.users.set(page.items);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  create(): void {
    this.dialog
      .open(UserDialog, { data: { roles: this.roles() } })
      .afterClosed()
      .subscribe((created) => {
        if (!created) return;
        this.toast.success(`${created.displayName} was added.`);
        this.load();
      });
  }

  setActive(user: UserDto, isActive: boolean): void {
    this.api.setUserActive(user.id, isActive).subscribe(() => {
      this.toast.success(`${user.displayName} ${isActive ? 'activated' : 'deactivated'}.`);
      this.load();
    });
  }

  editRoles(user: UserDto): void {
    this.selectedRoles = [...user.roles];
    this.editing.set(user);
  }

  toggleRole(name: string, checked: boolean): void {
    this.selectedRoles = checked
      ? [...this.selectedRoles, name]
      : this.selectedRoles.filter((r) => r !== name);
  }

  saveRoles(user: UserDto): void {
    this.api.setUserRoles(user.id, this.selectedRoles).subscribe(() => {
      this.toast.success('Roles updated. They take effect on their next sign-in.');
      this.editing.set(null);
      this.load();
    });
  }

  resetPassword(user: UserDto): void {
    this.dialog
      .open<ReasonDialog, ReasonData>(ReasonDialog, {
        data: {
          title: `Set a new password for ${user.displayName}`,
          message: 'Give them the new password yourself — it is not emailed to them.',
          label: 'New password (at least 10 characters)',
          confirmText: 'Set password',
          // The dialog performs the reset, so a password the server rejects leaves the dialog
          // open with the text still there instead of discarding it.
          submit: (password: string, ctx) => this.api.resetPassword(user.id, password, ctx),
        },
      })
      .afterClosed()
      .subscribe((done?: unknown) => {
        if (!done) return;
        this.toast.success(`New password set for ${user.displayName}.`);
      });
  }
}
