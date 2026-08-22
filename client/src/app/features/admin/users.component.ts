import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { PagedResult, RoleDto, UserDto } from '../../core/models';
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';
import { ReasonDialog } from '../../shared/dialogs';

@Component({
  selector: 'app-user-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSelectModule,
  ],
  template: `
    <h2 mat-dialog-title>New user</h2>
    <mat-dialog-content>
      <mat-form-field class="full">
        <mat-label>Username</mat-label>
        <input matInput [(ngModel)]="userName" />
      </mat-form-field>
      <mat-form-field class="full">
        <mat-label>Display name</mat-label>
        <input matInput [(ngModel)]="displayName" />
      </mat-form-field>
      <mat-form-field class="full">
        <mat-label>Email</mat-label>
        <input matInput type="email" [(ngModel)]="email" />
      </mat-form-field>
      <mat-form-field class="full">
        <mat-label>Temporary password</mat-label>
        <input matInput [(ngModel)]="password" />
        <mat-hint>At least 10 characters. They should change it on first sign-in.</mat-hint>
      </mat-form-field>
      <mat-form-field class="full">
        <mat-label>Roles</mat-label>
        <mat-select multiple [(ngModel)]="roles">
          @for (role of data.roles; track role.id) {
            <mat-option [value]="role.name">{{ role.name }}</mat-option>
          }
        </mat-select>
        <mat-hint>Only Worker and Administrator track shifts by default.</mat-hint>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" [disabled]="!valid()" (click)="confirm()">Create</button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    mat-dialog-content { min-width: min(440px, 82vw); padding-top: 8px !important; }
  `,
})
export class UserDialog {
  readonly ref = inject(MatDialogRef<UserDialog>);
  readonly data = inject<{ roles: RoleDto[] }>(MAT_DIALOG_DATA);

  userName = '';
  displayName = '';
  email = '';
  password = '';
  roles: string[] = [];

  valid = () =>
    this.userName.trim().length > 0 && this.displayName.trim().length > 0
    && this.email.trim().length > 0 && this.password.length >= 10;

  confirm(): void {
    this.ref.close({
      userName: this.userName.trim(),
      displayName: this.displayName.trim(),
      email: this.email.trim(),
      password: this.password,
      roles: this.roles,
    });
  }
}

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatMenuModule, MatSelectModule, MatSlideToggleModule, MatTableModule,
    PageHeaderComponent, ChipComponent, EmptyComponent, LoadingComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Users" subtitle="Accounts, roles and access.">
        <button matButton="filled" (click)="create()"><mat-icon>person_add</mat-icon> New user</button>
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
                  <div class="muted small">{{ u.userName }} · {{ u.email }}</div>
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
                  <mat-slide-toggle [checked]="u.isActive"
                                    (change)="setActive(u, $event.checked)" />
                </td>
              </ng-container>

              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef></th>
                <td mat-cell *matCellDef="let u" class="right">
                  <button matIconButton [matMenuTriggerFor]="menu"><mat-icon>more_vert</mat-icon></button>
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
            A permission change only takes effect the next time they sign in — permissions travel
            on the access token.
          </p>
          <div class="roles">
            @for (role of roles(); track role.id) {
              <mat-checkbox [checked]="selectedRoles.includes(role.name)"
                            (change)="toggleRole(role.name, $event.checked)">
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
    .right { text-align: right; }
    .top-gap { margin-top: 18px; }
    .roles { display: grid; gap: 6px; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); }
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
    this.dialog.open(UserDialog, { data: { roles: this.roles() } })
      .afterClosed()
      .subscribe((body) => {
        if (!body) return;
        this.api.createUser(body).subscribe(() => {
          this.toast.success(`${body.displayName} created.`);
          this.load();
        });
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
      .open(ReasonDialog, {
        data: {
          title: `Reset password for ${user.displayName}`,
          message: 'Give them the new password directly — it is not emailed.',
          label: 'New password (min 10 characters)',
          confirmText: 'Reset',
        },
      })
      .afterClosed()
      .subscribe((password?: string) => {
        if (!password || password.length < 10) {
          if (password) this.toast.error('Password must be at least 10 characters.');
          return;
        }
        this.api.resetPassword(user.id, password).subscribe(() =>
          this.toast.success('Password reset.'));
      });
  }
}
