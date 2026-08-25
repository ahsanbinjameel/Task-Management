import { Component, OnInit, computed, inject, signal } from '@angular/core';
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
import { FormSubmit } from '../../core/form-submit';
import { ToastService } from '../../core/toast.service';
import { PagedResult, RoleDto, UserDto, WorkforceState } from '../../core/models';

/** The states a person can be in, for the grid's filter. */
const WORKFORCE_STATES: WorkforceState[] = [
  'LoggedInShiftNotStarted', 'Available', 'Working', 'Break', 'Lunch', 'Meeting',
  'TemporarilyAway', 'ShiftEnded',
];
import { roleLabel, workforceStateLabel } from '../../core/labels';
import { SearchSelectComponent } from '../../shared/search-select.component';
import {
  ChipComponent,
  EmptyComponent,
  LoadingComponent,
  PageHeaderComponent,
} from '../../shared/ui';
import { ConfirmDialog, ConfirmData } from '../../shared/dialogs';
import {
  ColumnFilterComponent, ColumnFilterSpec, NoMatchesComponent, columnFilters,
} from '../../shared/column-filter.component';

/**
 * Correcting who someone is.
 *
 * Separate from the roles dialog on purpose: a misspelled surname and a change of authority are
 * different jobs, done by different people at different times, and merging them would put both
 * behind one Save and one audit row. The username is shown but not editable — it is what the person
 * signs in with and what every audit row was recorded against.
 */
@Component({
  selector: 'app-edit-user-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule,
  ],
  template: `
    <h2 mat-dialog-title>Edit {{ data.user.displayName }}</h2>
    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <p class="muted small who">
        Signs in as <strong>{{ data.user.userName }}</strong>. That cannot be changed — it is what
        every audit entry was recorded against.
      </p>

      <mat-form-field class="full">
        <mat-label>Full name</mat-label>
        <input matInput name="displayName" [(ngModel)]="displayName" cdkFocusInitial
               maxlength="200" />
        @if (form.fieldError('displayName'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Email (optional)</mat-label>
        <input matInput name="email" type="email" [(ngModel)]="email" maxlength="256" />
        @if (form.fieldError('email'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!displayName.trim() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving…' : 'Save' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    .who { margin: 0 0 14px; }
    mat-dialog-content { min-width: min(420px, 84vw); padding-top: 8px !important; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class EditUserDialog {
  readonly data = inject<{ user: UserDto }>(MAT_DIALOG_DATA);
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<EditUserDialog>);
  readonly form = new FormSubmit();

  displayName = this.data.user.displayName;
  email = this.data.user.email ?? '';

  save(): void {
    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.api.updateUser(this.data.user.id, {
        displayName: this.displayName.trim(),
        email: this.email.trim() || null,
      }, ctx),
      (user) => { this.ref.disableClose = false; this.ref.close(user ?? true); },
    );
  }
}

/**
 * Setting someone else's password.
 *
 * Replaces a reason dialog that collected it in a plain `<textarea>` — the password sat on screen
 * in full view, wrapped across lines, with no way to hide it. This asks for it twice instead, so a
 * masked field cannot hand over a typo that locks the person out of an account they have never
 * signed into.
 */
@Component({
  selector: 'app-set-password-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule,
  ],
  template: `
    <h2 mat-dialog-title>Set a new password for {{ data.displayName }}</h2>
    <mat-dialog-content>
      <p class="muted small note">
        Give them the new password yourself — it is not emailed to them. They stay signed in
        anywhere they already are until their session expires.
      </p>

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <mat-form-field class="full">
        <mat-label>New password (at least 10 characters)</mat-label>
        <input matInput name="newPassword" [type]="reveal() ? 'text' : 'password'"
               autocomplete="new-password" cdkFocusInitial [(ngModel)]="password" />
        <button matIconButton matSuffix type="button" tabindex="-1"
                (click)="reveal.set(!reveal())"
                [attr.aria-label]="reveal() ? 'Hide password' : 'Show password'">
          <mat-icon>{{ reveal() ? 'visibility_off' : 'visibility' }}</mat-icon>
        </button>
        @if (form.fieldError('newPassword'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Type it again</mat-label>
        <input matInput name="confirmPassword" [type]="reveal() ? 'text' : 'password'"
               autocomplete="new-password" [(ngModel)]="confirm" />
      </mat-form-field>

      @if (mismatch()) {
        <p class="mismatch" role="alert">The two passwords do not match.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!ready() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving…' : 'Set password' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    .note { margin: 0 0 12px; }
    mat-dialog-content { min-width: min(440px, 82vw); padding-top: 8px !important; }
    .mismatch { margin: 0; font-size: 13px; color: var(--tone-danger-fg); }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class SetPasswordDialog {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<SetPasswordDialog>);
  readonly data = inject<{ userId: number; displayName: string }>(MAT_DIALOG_DATA);
  readonly form = new FormSubmit();

  password = '';
  confirm = '';
  readonly reveal = signal(false);

  mismatch = (): boolean => this.confirm.length > 0 && this.password !== this.confirm;
  ready = (): boolean => this.password.length > 0 && this.password === this.confirm;

  save(): void {
    if (!this.ready()) return;

    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.api.resetPassword(this.data.userId, this.password, ctx),
      () => { this.ref.disableClose = false; this.ref.close(true); },
    );
  }
}

/**
 * Changing what someone can do.
 *
 * This was an inline panel that appeared *below* the table: on a long user list the edit opened
 * off-screen, and nothing tied the checkboxes back to the row that had been clicked. A dialog puts
 * the name and the choice in the same place, and performs the save itself so a refusal keeps the
 * selection.
 */
@Component({
  selector: 'app-roles-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatIconModule,
  ],
  template: `
    <h2 mat-dialog-title>What {{ data.user.displayName }} can do</h2>
    <mat-dialog-content>
      <p class="muted small note">
        A change only takes effect the next time they sign in — permissions travel on the access
        token.
      </p>

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <div class="roles">
        @for (role of data.roles; track role.id) {
          <mat-checkbox [checked]="selected().includes(role.name)"
                        (change)="toggle(role.name, $event.checked)">
            {{ roleLabel(role.name) }}
            <span class="muted small"> — {{ role.permissions.length }} permissions</span>
          </mat-checkbox>
        }
      </div>

      @if (selected().length === 0) {
        <p class="warn-note" role="alert">
          With no roles at all they can sign in but will not be able to see or do anything.
        </p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving…' : 'Save roles' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .note { margin: 0 0 12px; }
    mat-dialog-content { min-width: min(460px, 84vw); padding-top: 8px !important; }
    .roles { display: grid; gap: 6px; }
    .warn-note {
      margin: 14px 0 0; padding: 9px 11px; border-radius: 8px; font-size: 13px;
      background: var(--tone-warn-bg); color: var(--tone-warn-fg);
    }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class RolesDialog {
  readonly roleLabel = roleLabel;

  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<RolesDialog>);
  readonly data = inject<{ user: UserDto; roles: RoleDto[] }>(MAT_DIALOG_DATA);
  readonly form = new FormSubmit();

  readonly selected = signal<string[]>([...this.data.user.roles]);

  toggle(name: string, checked: boolean): void {
    this.selected.update((list) =>
      checked ? [...list, name] : list.filter((r) => r !== name));
  }

  save(): void {
    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.api.setUserRoles(this.data.user.id, this.selected(), ctx),
      (user) => { this.ref.disableClose = false; this.ref.close(user ?? true); },
    );
  }
}

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

      <!--
        Masked by default. An administrator setting someone's first password has no business
        reading it off a shared screen, and this dialog is opened in front of people often enough
        that "nobody is looking" is not a safe assumption. The reveal is deliberate and one click
        away, because the password does have to be read out to hand it over.
      -->
      <mat-form-field class="full">
        <mat-label>Temporary password</mat-label>
        <input
          matInput
          name="password"
          [type]="showPassword() ? 'text' : 'password'"
          autocomplete="new-password"
          [(ngModel)]="password"
          (ngModelChange)="clearField('password')"
        />
        <button matIconButton matSuffix type="button" tabindex="-1"
                (click)="showPassword.set(!showPassword())"
                [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'">
          <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
        </button>
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
  readonly showPassword = signal(false);
  roles: string[] = [];
  readonly roleOptions = this.data.roles.map((role) => ({ value: role.name, label: roleLabel(role.name) }));

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
    ColumnFilterComponent,
    NoMatchesComponent,
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
        } @else if (users().length === 0 && !filters.any()) {
          <!-- Only when there really are none. With a filter set the grid stays up: this message
               once claimed "No accounts yet" while eight accounts existed and one filter was on. -->
          <app-empty message="No accounts yet" icon="group"
                     hint="Add someone before they can raise a request or be given work." />
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
                      <span class="chip tone-neutral">{{ roleLabel(role) }}</span>
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
                    <button mat-menu-item (click)="editUser(u)">
                      <mat-icon>edit</mat-icon><span>Edit details</span>
                    </button>
                    <button mat-menu-item (click)="editRoles(u)">
                      <mat-icon>badge</mat-icon><span>Change roles</span>
                    </button>
                    <button mat-menu-item (click)="resetPassword(u)">
                      <mat-icon>lock_reset</mat-icon><span>Reset password</span>
                    </button>
                  </mat-menu>
                </td>
              </ng-container>

              <!-- The filter row, generated from the same column list as the header above it. -->
              @for (column of columns; track column) {
                <ng-container [matColumnDef]="column + '_filter'">
                  <th mat-header-cell *matHeaderCellDef class="filter-cell">
                    <app-column-filter [spec]="specs()[column]" [value]="filters.value(column)"
                                       (changed)="filters.set(specs()[column], column, $event)" />
                  </th>
                </ng-container>
              }

              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-header-row *matHeaderRowDef="filterRow" class="filter-row"></tr>
              <tr mat-row *matRowDef="let row; columns: columns"></tr>
            </table>
          </div>

          @if (users().length === 0) {
            <app-no-matches message="No accounts match those filters."
                            (clear)="filters.clear()" />
          }
        }
      </div>

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
  /** The wording layer — `AssignmentManager` is not a word anyone says. */
  readonly roleLabel = roleLabel;

  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  readonly users = signal<UserDto[]>([]);
  readonly roles = signal<RoleDto[]>([]);
  readonly loading = signal(true);

  readonly columns = ['name', 'roles', 'state', 'active', 'actions'];
  readonly filterRow = this.columns.map((c) => c + '_filter');

  /**
   * The people grid had no filtering at all — fine at seven accounts, unusable at two hundred.
   * Roles and state are exact matches because both come from short, known lists; the name is a
   * contains match across display name and username, because people search for either.
   */
  readonly filters = columnFilters(() => this.load());

  readonly specs = computed<Record<string, ColumnFilterSpec>>(() => ({
    name: { key: 'name', kind: 'text', placeholder: 'Name or username' },
    roles: {
      key: 'roles', kind: 'select', placeholder: 'Any role',
      options: this.roles().map((r) => ({ value: r.name, label: roleLabel(r.name) })),
    },
    state: {
      key: 'state', kind: 'select', placeholder: 'Any',
      options: WORKFORCE_STATES.map((v) => ({ value: v, label: workforceStateLabel(v) })),
    },
    active: {
      key: 'active', kind: 'select', placeholder: 'Any',
      options: [{ value: 'true', label: 'Active' }, { value: 'false', label: 'Turned off' }],
    },
  }));

  ngOnInit(): void {
    this.load();
    this.api.roles().subscribe({ next: (r) => this.roles.set(r), error: () => undefined });
  }

  load(): void {
    this.api.users({ pageSize: 200, ...this.filters.asObject() }).subscribe({
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
    // Switching someone back on is harmless. Switching them off locks them out of the system
    // mid-shift, and a toggle is the easiest control in the app to hit by accident, so only the
    // destructive direction asks. The toggle is put back if the answer is no — otherwise it sits
    // there showing a state the server never accepted.
    if (isActive) {
      this.apply(user, true);
      return;
    }

    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: `Turn off ${user.displayName}'s account?`,
          message:
            'They will be signed out and will not be able to sign in again. Work already assigned '
            + 'to them stays assigned.',
          confirmText: 'Turn it off',
          danger: true,
        },
      })
      .afterClosed()
      .subscribe((confirmed?: boolean) => {
        if (confirmed) this.apply(user, false);
        else this.load();
      });
  }

  private apply(user: UserDto, isActive: boolean): void {
    this.api.setUserActive(user.id, isActive).subscribe(() => {
      this.toast.success(`${user.displayName} ${isActive ? 'activated' : 'deactivated'}.`);
      this.load();
    });
  }

  editUser(user: UserDto): void {
    this.dialog
      .open<EditUserDialog, { user: UserDto }>(EditUserDialog, { data: { user } })
      .afterClosed()
      .subscribe((saved?: unknown) => {
        if (!saved) return;
        this.toast.success('Saved.');
        this.load();
      });
  }

  editRoles(user: UserDto): void {
    this.dialog
      .open<RolesDialog, { user: UserDto; roles: RoleDto[] }>(RolesDialog, {
        data: { user, roles: this.roles() },
      })
      .afterClosed()
      .subscribe((saved?: unknown) => {
        if (!saved) return;
        this.toast.success('Roles updated. They take effect on their next sign-in.');
        this.load();
      });
  }

  resetPassword(user: UserDto): void {
    this.dialog
      .open<SetPasswordDialog, { userId: number; displayName: string }>(SetPasswordDialog, {
        data: { userId: user.id, displayName: user.displayName },
      })
      .afterClosed()
      .subscribe((done?: unknown) => {
        if (!done) return;
        this.toast.success(`New password set for ${user.displayName}.`);
      });
  }
}
