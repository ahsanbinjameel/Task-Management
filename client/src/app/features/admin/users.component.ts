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
import { ChipComponent, PageHeaderComponent } from '../../shared/ui';
import { ConfirmDialog, ConfirmData } from '../../shared/dialogs';
import { columnFilters } from '../../shared/column-filter.component';
import {
  DataGridComponent, GridCellDirective, GridColumn,
} from '../../shared/data-grid.component';

/**
 * Everything about one account, in one place.
 *
 * It used to be three: a rename dialog, a "reset password" item in the row menu, and a separate
 * roles dialog. Two of those have been folded together here, because "change this person" is one
 * job to the person doing it and three menu items is three chances to pick the wrong one.
 *
 * **Roles are still separate**, deliberately: granting authority is a different decision from
 * fixing a surname, usually made by a different person, and it deserves its own audit row.
 *
 * The password field sets a new one; it never shows the current one, because there is nothing to
 * show. Passwords are stored as one-way PBKDF2 hashes, so no screen and no API can read one back -
 * only replace it.
 */
@Component({
  selector: 'app-edit-user-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.user.displayName }}</h2>
    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <mat-form-field class="full">
        <mat-label>Username</mat-label>
        <input matInput name="userName" [(ngModel)]="userName" cdkFocusInitial maxlength="100"
               autocomplete="off" />
        @if (form.fieldError('userName'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Full name</mat-label>
        <input matInput name="displayName" [(ngModel)]="displayName" maxlength="200" />
        @if (form.fieldError('displayName'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Email (optional)</mat-label>
        <input matInput name="email" type="email" [(ngModel)]="email" maxlength="256" />
        @if (form.fieldError('email'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <h3 class="section">Password</h3>
      <mat-form-field class="full">
        <mat-label>Set a new password</mat-label>
        <input matInput name="newPassword" [type]="reveal() ? 'text' : 'password'"
               autocomplete="new-password" [(ngModel)]="newPassword" maxlength="200" />
        <button matIconButton matSuffix type="button" tabindex="-1"
                (click)="reveal.set(!reveal())"
                [attr.aria-label]="reveal() ? 'Hide password' : 'Show password'">
          <mat-icon>{{ reveal() ? 'visibility_off' : 'visibility' }}</mat-icon>
        </button>
        @if (form.fieldError('newPassword'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      @if (newPassword) {
        <mat-form-field class="full">
          <mat-label>Type it again</mat-label>
          <input matInput name="confirmPassword" [type]="reveal() ? 'text' : 'password'"
                 autocomplete="new-password" [(ngModel)]="confirm" maxlength="200" />
        </mat-form-field>

        <p class="warn-note" role="note">
          <mat-icon>info_outline</mat-icon>
          <span>
            Setting a password signs them out everywhere and clears any lockout. Give them the new
            one yourself - it is not emailed.
          </span>
        </p>
      }

      @if (mismatch()) {
        <p class="mismatch" role="alert">The two passwords do not match.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!ready() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving...' : 'Save' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    .section {
      margin: 18px 0 4px; font-size: 12px; font-weight: 600; letter-spacing: .04em;
      text-transform: uppercase; color: var(--text-muted);
    }
    .note { margin: 0 0 12px; }
    .mismatch { margin: 8px 0 0; font-size: 13px; color: var(--tone-danger-fg); }
    mat-dialog-content { min-width: min(460px, 86vw); padding-top: 8px !important; }
    .warn-note {
      display: flex; gap: 8px; align-items: flex-start; margin: 4px 0 0;
      padding: 9px 11px; border-radius: 8px; font-size: 12.5px; line-height: 1.45;
      background: var(--tone-warn-bg); color: var(--tone-warn-fg);
    }
    .warn-note mat-icon { font-size: 16px; width: 16px; height: 16px; flex: none; margin-top: 1px; }
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

  userName = this.data.user.userName;
  displayName = this.data.user.displayName;
  email = this.data.user.email ?? '';
  newPassword = '';
  confirm = '';
  readonly reveal = signal(false);

  mismatch = (): boolean => !!this.newPassword && !!this.confirm && this.newPassword !== this.confirm;

  ready = (): boolean =>
    this.userName.trim().length > 0
    && this.displayName.trim().length > 0
    && (!this.newPassword || this.newPassword === this.confirm);

  save(): void {
    if (!this.ready()) return;

    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.api.updateUser(this.data.user.id, {
        userName: this.userName.trim(),
        displayName: this.displayName.trim(),
        email: this.email.trim() || null,
        newPassword: this.newPassword || null,
      }, ctx),
      (user) => { this.ref.disableClose = false; this.ref.close(user ?? true); },
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
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatSlideToggleModule,
    PageHeaderComponent,
    ChipComponent,
    DataGridComponent,
    GridCellDirective,
  ],
  template: `
    <div class="page fills">
      <app-page-header title="Users">
        <button matButton="filled" (click)="create()">
          <mat-icon>person_add</mat-icon> New user
        </button>
      </app-page-header>

      <app-data-grid
        [rows]="users()" [columns]="columns()"
        [loading]="loading()" [filters]="filters"
        emptyMessage="No accounts yet" emptyIcon="group"
        noMatchesMessage="No accounts match those filters.">

        <ng-template gridCell="name" let-u>
          <strong>{{ u.displayName }}</strong>
          <div class="muted small">{{ u.userName }}{{ u.email ? ' · ' + u.email : '' }}</div>
        </ng-template>

        <ng-template gridCell="roles" let-u>
          <div class="row row-wrap" style="gap:5px">
            @for (role of u.roles; track role) {
              <span class="chip tone-neutral">{{ roleLabel(role) }}</span>
            } @empty {
              <span class="muted small">None</span>
            }
          </div>
        </ng-template>

        <ng-template gridCell="state" let-u>
          <app-chip [value]="u.workforceState" kind="workforce" [dot]="true" />
        </ng-template>

        <ng-template gridCell="active" let-u>
          <mat-slide-toggle [checked]="u.isActive" (change)="setActive(u, $event.checked)" />
        </ng-template>

        <ng-template gridCell="actions" let-u>
          <button matIconButton [matMenuTriggerFor]="menu">
            <mat-icon>more_vert</mat-icon>
          </button>
          <mat-menu #menu="matMenu">
            <button mat-menu-item (click)="editUser(u)">
              <mat-icon>edit</mat-icon><span>Edit account</span>
            </button>
            <button mat-menu-item (click)="editRoles(u)">
              <mat-icon>badge</mat-icon><span>Change roles</span>
            </button>
          </mat-menu>
        </ng-template>
      </app-data-grid>
    </div>
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

  /**
   * The people grid had no filtering at all — fine at seven accounts, unusable at two hundred.
   * Roles and state are exact matches because both come from short, known lists; the name is a
   * contains match across display name and username, because people search for either.
   */
  readonly filters = columnFilters(() => this.load());

  readonly columns = computed<GridColumn<UserDto>[]>(() => [
    {
      key: 'name', header: 'Name', minWidth: 220,
      filter: { kind: 'text', placeholder: 'Name or username' },
    },
    {
      // By id, not name: a role is administrator-named and could contain a comma, which is the one
      // character the multi-value format cannot carry.
      key: 'roles', header: 'Roles',
      filter: {
        kind: 'select', placeholder: 'Any role',
        options: this.roles().map((r) => ({ value: r.id, label: roleLabel(r.name) })),
      },
    },
    {
      key: 'state', header: 'Availability',
      filter: {
        kind: 'select', placeholder: 'Any',
        options: WORKFORCE_STATES.map((v) => ({ value: v, label: workforceStateLabel(v) })),
      },
    },
    {
      key: 'active', header: 'Active',
      filter: {
        kind: 'select', placeholder: 'Any',
        options: [{ value: 'true', label: 'Active' }, { value: 'false', label: 'Turned off' }],
      },
    },
    { key: 'actions', header: 'Actions', headerHidden: true, align: 'right' },
  ]);

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
        this.toast.success('Account updated.');
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
}
