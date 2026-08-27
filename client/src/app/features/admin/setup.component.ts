import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpContext } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService, SavePauseReasonBody } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { FormSubmit } from '../../core/form-submit';
import {
  PauseCategory, SetupClientDto, SetupDepartmentDto, SetupPauseReasonDto, SetupTeamDto,
  WorkforceState,
} from '../../core/models';
import { pauseCategoryLabel, workforceStateLabel } from '../../core/labels';
import { enumOptions, SearchSelectComponent } from '../../shared/search-select.component';
import { ConfirmDialog, ConfirmData } from '../../shared/dialogs';
import { EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';

/** A name, and optionally a code or a parent. Covers clients, departments and teams. */
interface SimpleEditData {
  title: string;
  nameLabel: string;
  name: string;
  code?: string | null;
  showCode?: boolean;
  departmentId?: number | null;
  departments?: SetupDepartmentDto[];
  save: (value: { name: string; code?: string | null; departmentId?: number | null },
        ctx: HttpContext) => import('rxjs').Observable<unknown>;
}

/**
 * Add or rename one piece of reference data.
 *
 * One dialog for three lists rather than three near-identical ones: they differ by a label and at
 * most one extra field, and three copies would drift the moment one of them grew a validation rule.
 */
@Component({
  selector: 'app-simple-edit-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <mat-form-field class="full">
        <mat-label>{{ data.nameLabel }}</mat-label>
        <input matInput name="name" [(ngModel)]="name" cdkFocusInitial maxlength="200" />
        @if (form.fieldError('name'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      @if (data.showCode) {
        <mat-form-field class="full">
          <mat-label>Short code (optional)</mat-label>
          <input matInput name="code" [(ngModel)]="code" maxlength="50" />
        </mat-form-field>
      }

      @if (data.departments) {
        <app-search-select class="full" label="Department (optional)" name="department"
                           nullLabel="No department"
                           [options]="departmentOptions()" [(ngModel)]="departmentId" />
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!name.trim() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving…' : 'Save' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%;}
    mat-dialog-content { min-width: min(440px, 84vw); padding-top: 8px !important; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class SimpleEditDialog {
  readonly data = inject<SimpleEditData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<SimpleEditDialog>);
  readonly form = new FormSubmit();

  name = this.data.name;
  code = this.data.code ?? '';
  departmentId = this.data.departmentId ?? null;

  departmentOptions = () =>
    (this.data.departments ?? []).map((d) => ({ value: d.id, label: d.name }));

  save(): void {
    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.data.save(
        {
          name: this.name.trim(),
          code: this.code.trim() || null,
          departmentId: this.departmentId,
        },
        ctx),
      (saved) => { this.ref.disableClose = false; this.ref.close(saved ?? true); },
    );
  }
}

/**
 * A pause reason is the one lookup where every field changes behaviour.
 *
 * "Blocks the task" decides whether the work is genuinely stuck or merely unattended, and "away
 * state" decides whether the person leaves the floor as well. Getting either wrong silently
 * corrupts a report rather than showing a wrong word, so the form explains both rather than
 * offering two unlabelled switches.
 */
@Component({
  selector: 'app-pause-reason-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, MatCheckboxModule, SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.reason ? 'Change this reason' : 'Add a pause reason' }}</h2>
    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <mat-form-field class="full">
        <mat-label>What the worker picks</mat-label>
        <input matInput name="name" [(ngModel)]="name" cdkFocusInitial maxlength="200" />
        @if (form.fieldError('name'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <app-search-select class="full" label="Grouping" name="category"
                         [options]="categoryOptions" [(ngModel)]="category" />

      <div class="switches">
        <mat-checkbox [(ngModel)]="isBlocker" name="blocker">
          The task is stuck
        </mat-checkbox>

        <mat-checkbox [(ngModel)]="requiresComment" name="comment">
          Make them explain
        </mat-checkbox>
      </div>

      <app-search-select class="full" label="Where the person goes" name="away"
                         nullLabel="Nowhere — they stay on shift and free for other work"
                         [options]="awayOptions" [(ngModel)]="awayState" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!name.trim() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving…' : 'Save' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%;}
    mat-dialog-content { min-width: min(520px, 86vw); padding-top: 8px !important; }
    .switches { display: grid; gap: 10px; margin: 4px 0 16px; }
    .hint { display: block; margin-left: 0; }
    .note { margin: 6px 0 0; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class PauseReasonDialog {
  readonly data = inject<{ reason: SetupPauseReasonDto | null }>(MAT_DIALOG_DATA);
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<PauseReasonDialog>);
  readonly form = new FormSubmit();

  private readonly categories: PauseCategory[] = [
    'OtherWorkUrgent', 'WaitingForSomeone', 'WaitingForClient', 'CannotContinue',
    'Meeting', 'Break', 'Lunch', 'EndOfShift', 'Other',
  ];

  /**
   * ShiftEnded is deliberately absent: only the end-shift operation may set it, and the server
   * refuses it here. Offering a choice the API rejects would be a trap.
   */
  private readonly awayStates: WorkforceState[] = [
    'Available', 'Break', 'Lunch', 'Meeting', 'TemporarilyAway',
  ];

  readonly categoryOptions = this.categories.map((c) => ({ value: c, label: pauseCategoryLabel(c) }));
  readonly awayOptions = this.awayStates.map((s) => ({ value: s, label: workforceStateLabel(s) }));

  name = this.data.reason?.name ?? '';
  category: PauseCategory = this.data.reason?.category ?? 'Other';
  isBlocker = this.data.reason?.isBlocker ?? false;
  requiresComment = this.data.reason?.requiresComment ?? false;
  awayState: WorkforceState | null = this.data.reason?.awayState ?? null;

  save(): void {
    const body: SavePauseReasonBody = {
      name: this.name.trim(),
      category: this.category,
      isBlocker: this.isBlocker,
      requiresComment: this.requiresComment,
      awayState: this.awayState,
    };

    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.data.reason
        ? this.api.updatePauseReason(this.data.reason.id, body, ctx)
        : this.api.createPauseReason(body, ctx),
      (saved) => { this.ref.disableClose = false; this.ref.close(saved ?? true); },
    );
  }
}

/**
 * The administrator's setup screen: the lists everything else picks from.
 *
 * Grouped into tabs rather than one long page because these are unrelated jobs done at different
 * times — a client is added when a new customer arrives, a pause reason when the way people work
 * changes — and stacking them means scrolling past four lists to reach the fifth.
 *
 * Every list shows what already points at each row, and offers **retire** rather than delete. That
 * is the whole design: reference data is referenced by history, and a client removed from under
 * three months of requests turns those reports into blanks.
 */
@Component({
  selector: 'app-setup',
  standalone: true,
  imports: [
    MatButtonModule, MatIconModule, MatSlideToggleModule, MatTabsModule, MatTooltipModule,
    EmptyComponent, LoadingComponent, PageHeaderComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Setup data" />

      <mat-tab-group animationDuration="0ms">
        <!-- --- clients ------------------------------------------------------------------- -->
        <mat-tab label="Clients">
          <div class="tab">
            <div class="row bar">
              <span class="spacer"></span>
              <button matButton="filled" (click)="addClient()">
                <mat-icon>add</mat-icon> Add a client
              </button>
            </div>

            @if (loading()) {
              <app-loading />
            } @else if (clients().length === 0) {
              <app-empty message="No clients yet" icon="apartment" />
            } @else {
              <div class="card rows">
                @for (c of clients(); track c.id) {
                  <div class="entry" [class.off]="!c.isActive">
                    <div class="meta">
                      <span class="name">{{ c.name }}</span>
                      @if (c.code) { <span class="muted small">{{ c.code }}</span> }
                      <span class="muted small">{{ used(c.requestCount, 'request') }}</span>
                    </div>
                    <button matIconButton (click)="editClient(c)" matTooltip="Rename">
                      <mat-icon>edit</mat-icon>
                    </button>
                    <mat-slide-toggle [checked]="c.isActive"
                                      (change)="toggleClient(c, $event.checked)"
                                      [matTooltip]="c.isActive ? 'In use' : 'Retired'" />
                  </div>
                }
              </div>
            }
          </div>
        </mat-tab>

        <!-- --- pause reasons ------------------------------------------------------------- -->
        <mat-tab label="Pause reasons">
          <div class="tab">
            <div class="row bar">
              <span class="spacer"></span>
              <button matButton="filled" (click)="addPauseReason()">
                <mat-icon>add</mat-icon> Add a reason
              </button>
            </div>

            @if (loading()) {
              <app-loading />
            } @else {
              <div class="card rows">
                @for (r of pauseReasons(); track r.id) {
                  <div class="entry" [class.off]="!r.isActive">
                    <div class="meta">
                      <span class="name">{{ r.name }}</span>
                      <span class="tags">
                        <span class="tag">{{ categoryLabel(r.category) }}</span>
                        @if (r.isBlocker) { <span class="tag danger">Task stuck</span> }
                        @if (r.requiresComment) { <span class="tag">Note required</span> }
                        @if (r.awayState) {
                          <span class="tag warn">Away: {{ stateLabel(r.awayState) }}</span>
                        }
                      </span>
                      <span class="muted small">{{ used(r.timesUsed, 'time') }}</span>
                    </div>
                    <button matIconButton (click)="editPauseReason(r)" matTooltip="Change">
                      <mat-icon>edit</mat-icon>
                    </button>
                    <mat-slide-toggle [checked]="r.isActive"
                                      (change)="togglePauseReason(r, $event.checked)"
                                      [matTooltip]="r.isActive ? 'Offered' : 'Retired'" />
                  </div>
                }
              </div>
            }
          </div>
        </mat-tab>

        <!-- --- departments --------------------------------------------------------------- -->
        <mat-tab label="Departments">
          <div class="tab">
            <div class="row bar">
              <span class="spacer"></span>
              <button matButton="filled" (click)="addDepartment()">
                <mat-icon>add</mat-icon> Add a department
              </button>
            </div>

            @if (loading()) {
              <app-loading />
            } @else if (departments().length === 0) {
              <app-empty message="No departments yet" icon="corporate_fare" />
            } @else {
              <div class="card rows">
                @for (d of departments(); track d.id) {
                  <div class="entry" [class.off]="!d.isActive">
                    <div class="meta">
                      <span class="name">{{ d.name }}</span>
                      <span class="muted small">{{ used(d.teamCount, 'team') }}</span>
                    </div>
                    <button matIconButton (click)="editDepartment(d)" matTooltip="Rename">
                      <mat-icon>edit</mat-icon>
                    </button>
                    <mat-slide-toggle [checked]="d.isActive"
                                      (change)="toggleDepartment(d, $event.checked)" />
                  </div>
                }
              </div>
            }
          </div>
        </mat-tab>

        <!-- --- teams --------------------------------------------------------------------- -->
        <mat-tab label="Teams">
          <div class="tab">
            <div class="row bar">
              <span class="spacer"></span>
              <button matButton="filled" (click)="addTeam()">
                <mat-icon>add</mat-icon> Add a team
              </button>
            </div>

            @if (loading()) {
              <app-loading />
            } @else if (teams().length === 0) {
              <app-empty message="No teams yet" icon="groups" />
            } @else {
              <div class="card rows">
                @for (t of teams(); track t.id) {
                  <div class="entry" [class.off]="!t.isActive">
                    <div class="meta">
                      <span class="name">{{ t.name }}</span>
                      <span class="muted small">{{ t.departmentName ?? 'No department' }}</span>
                    </div>
                    <button matIconButton (click)="editTeam(t)" matTooltip="Change">
                      <mat-icon>edit</mat-icon>
                    </button>
                    <mat-slide-toggle [checked]="t.isActive"
                                      (change)="toggleTeam(t, $event.checked)" />
                  </div>
                }
              </div>
            }
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: `
    .tab { padding-top: 16px; }
    .bar { align-items: flex-start; gap: 12px; margin-bottom: 12px; }
    .bar p { margin: 0; max-width: 60ch; }
    .rows { overflow: hidden; }
    .entry {
      display: flex; align-items: center; gap: 12px;
      padding: 10px 14px; border-bottom: 1px solid var(--border);
    }
    .entry:last-child { border-bottom: 0; }
    .entry.off { opacity: .55; }
    .meta { flex: 1 1 auto; display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .name { font-weight: 600; }
    .tags { display: flex; gap: 5px; flex-wrap: wrap; }
    .tag {
      font-size: 11.5px; padding: 1px 7px; border-radius: 999px;
      background: var(--tone-neutral-bg, #eef0f3); color: var(--text-muted);
    }
    .tag.danger { background: var(--tone-danger-bg); color: var(--tone-danger-fg); }
    .tag.warn { background: var(--tone-warn-bg); color: var(--tone-warn-fg); }
  `,
})
export class SetupComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly clients = signal<SetupClientDto[]>([]);
  readonly departments = signal<SetupDepartmentDto[]>([]);
  readonly teams = signal<SetupTeamDto[]>([]);
  readonly pauseReasons = signal<SetupPauseReasonDto[]>([]);

  readonly categoryLabel = pauseCategoryLabel;
  readonly stateLabel = workforceStateLabel;

  /** "3 requests" / "not used yet" — the answer to "is this safe to change?". */
  used(count: number, noun: string): string {
    if (count === 0) return 'Not used yet';
    return `${count} ${noun}${count === 1 ? '' : 's'}`;
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.setupClients().subscribe({
      next: (c) => { this.clients.set(c); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
    this.api.setupDepartments().subscribe((d) => this.departments.set(d));
    this.api.setupTeams().subscribe((t) => this.teams.set(t));
    this.api.setupPauseReasons().subscribe((r) => this.pauseReasons.set(r));
  }

  // --- clients -----------------------------------------------------------------------------

  addClient(): void {
    this.openSimple({
      title: 'Add a client',
      nameLabel: 'Client name',
      name: '',
      showCode: true,
      save: (v, ctx) => this.api.createClient({ name: v.name, code: v.code }, ctx),
    });
  }

  editClient(client: SetupClientDto): void {
    this.openSimple({
      title: `Rename ${client.name}`,
      nameLabel: 'Client name',
      name: client.name,
      code: client.code,
      showCode: true,
      save: (v, ctx) => this.api.updateClient(client.id, { name: v.name, code: v.code }, ctx),
    });
  }

  toggleClient(client: SetupClientDto, isActive: boolean): void {
    this.retire(
      isActive, client.name, client.requestCount, 'request',
      'It stops being offered on new requests. The ones already against it keep it.',
      (ctx) => this.api.setClientActive(client.id, isActive, ctx));
  }

  // --- pause reasons -----------------------------------------------------------------------

  addPauseReason(): void {
    this.dialog.open(PauseReasonDialog, { data: { reason: null }, width: 'min(560px, 92vw)' })
      .afterClosed().subscribe((saved) => this.after(saved, 'Pause reason added.'));
  }

  editPauseReason(reason: SetupPauseReasonDto): void {
    this.dialog.open(PauseReasonDialog, { data: { reason }, width: 'min(560px, 92vw)' })
      .afterClosed().subscribe((saved) => this.after(saved, 'Pause reason updated.'));
  }

  togglePauseReason(reason: SetupPauseReasonDto, isActive: boolean): void {
    this.retire(
      isActive, reason.name, reason.timesUsed, 'time',
      'Workers stop being offered it. Pauses already recorded against it keep it.',
      (ctx) => this.api.setPauseReasonActive(reason.id, isActive, ctx));
  }

  // --- departments and teams ---------------------------------------------------------------

  addDepartment(): void {
    this.openSimple({
      title: 'Add a department',
      nameLabel: 'Department name',
      name: '',
      save: (v, ctx) => this.api.createDepartment({ name: v.name }, ctx),
    });
  }

  editDepartment(department: SetupDepartmentDto): void {
    this.openSimple({
      title: `Rename ${department.name}`,
      nameLabel: 'Department name',
      name: department.name,
      save: (v, ctx) => this.api.updateDepartment(department.id, { name: v.name }, ctx),
    });
  }

  toggleDepartment(department: SetupDepartmentDto, isActive: boolean): void {
    this.retire(
      isActive, department.name, department.teamCount, 'team',
      'It stops being offered when teams are set up.',
      (ctx) => this.api.setDepartmentActive(department.id, isActive, ctx));
  }

  addTeam(): void {
    this.openSimple({
      title: 'Add a team',
      nameLabel: 'Team name',
      name: '',
      departmentId: null,
      departments: this.departments(),
      save: (v, ctx) => this.api.createTeam({ name: v.name, departmentId: v.departmentId }, ctx),
    });
  }

  editTeam(team: SetupTeamDto): void {
    this.openSimple({
      title: `Change ${team.name}`,
      nameLabel: 'Team name',
      name: team.name,
      departmentId: team.departmentId ?? null,
      departments: this.departments(),
      save: (v, ctx) => this.api.updateTeam(team.id, { name: v.name, departmentId: v.departmentId }, ctx),
    });
  }

  toggleTeam(team: SetupTeamDto, isActive: boolean): void {
    this.retire(
      isActive, team.name, 0, 'person',
      'It stops being offered when people are set up.',
      (ctx) => this.api.setTeamActive(team.id, isActive, ctx));
  }

  // --- shared ------------------------------------------------------------------------------

  private openSimple(data: SimpleEditData): void {
    this.dialog.open(SimpleEditDialog, { data, width: 'min(480px, 92vw)' })
      .afterClosed().subscribe((saved) => this.after(saved, 'Saved.'));
  }

  /**
   * Turning something back on is harmless; retiring it is what needs a sentence, and the sentence
   * has to say what happens to the history rather than just "are you sure?".
   */
  private retire(
    isActive: boolean,
    name: string,
    count: number,
    noun: string,
    consequence: string,
    submit: (ctx: HttpContext) => import('rxjs').Observable<unknown>,
  ): void {
    if (isActive) {
      submit(new HttpContext()).subscribe({
        next: () => { this.toast.success(`${name} is in use again.`); this.load(); },
        error: () => this.load(),
      });
      return;
    }

    const attached = count > 0
      ? ` ${count} ${noun}${count === 1 ? '' : 's'} already point at it, and ${count === 1 ? 'it keeps' : 'they keep'} it.`
      : '';

    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: `Retire ${name}?`,
          message: consequence + attached
            + ' Nothing is deleted — you can turn it back on at any time.',
          confirmText: 'Retire it',
          submit,
        },
      })
      .afterClosed()
      .subscribe((done?: unknown) => {
        // The toggle already moved; reload either way so a cancelled retire snaps back.
        if (done) this.toast.success(`${name} retired.`);
        this.load();
      });
  }

  private after(saved: unknown, message: string): void {
    if (!saved) return;
    this.toast.success(message);
    this.load();
  }
}
