import { Component, computed, inject, signal } from '@angular/core';
import { HttpContext } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { FormSubmit } from '../../core/form-submit';
import { ImpersonationTargetDto } from '../../core/models';

/**
 * Pick somebody to act as, for a demonstration or to see what a person is describing.
 *
 * Two things this dialog is careful to say, because both are easy to assume wrongly.
 *
 * It is not a preview. The session it starts is a real session against the real database, so
 * anything done while acting is real work — which is the point, since a walkthrough that wrote
 * nowhere would demonstrate nothing.
 *
 * And it is not a way to keep your own authority under a different name. The session carries that
 * person's permissions and none of the administrator's, so a reviewer's screen looks exactly as it
 * looks to the reviewer.
 */
@Component({
  selector: 'app-act-as-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Act as somebody else</h2>

    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <p class="note">
        <mat-icon>info</mat-icon>
        <span>
          You will see the app exactly as they do, with their permissions and none of yours.
          Anything you do is <strong>real work</strong>, recorded against them with your name
          alongside it.
        </span>
      </p>

      @if (people().length > 6) {
        <input class="search" type="text" placeholder="Search" [value]="search()"
               (input)="search.set($any($event.target).value)" aria-label="Search people" />
      }

      @if (loading()) {
        <p class="muted small pad">Loading…</p>
      } @else {
        <div class="people">
          @for (person of visible(); track person.id) {
            <button type="button" class="person" [disabled]="form.busy()" (click)="actAs(person)">
              <span class="detail">
                <span class="name">{{ person.displayName }}</span>
                <span class="muted small">
                  {{ person.userName }}
                  @if (person.roles.length) { · {{ person.roles.join(', ') }} }
                </span>
              </span>
              <mat-icon>chevron_right</mat-icon>
            </button>
          } @empty {
            <p class="muted small pad">
              @if (people().length === 0) {
                There is nobody to act as. Other administrators are deliberately excluded.
              } @else {
                Nobody matches "{{ search() }}".
              }
            </p>
          }
        </div>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(460px, 84vw); padding-top: 8px !important; }
    .note {
      display: flex; gap: 8px; align-items: flex-start; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13px; line-height: 1.45;
      background: var(--tone-warn-bg); color: var(--tone-warn-fg);
    }
    .note mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    .search {
      width: 100%; margin-bottom: 8px; padding: 7px 10px; font: inherit; font-size: 13px;
      border: 1px solid var(--border); border-radius: 7px;
      background: var(--surface); color: var(--text);
    }
    .people { max-height: 320px; overflow-y: auto; }
    .person {
      display: flex; align-items: center; gap: 10px; width: 100%; text-align: left;
      padding: 9px 10px; margin-bottom: 3px; cursor: pointer;
      border: 1px solid var(--border); border-radius: 8px;
      background: var(--surface); color: var(--text); font: inherit;
    }
    .person:hover:not(:disabled) { background: var(--surface-sunken); }
    .person:disabled { opacity: .6; cursor: default; }
    .detail { display: flex; flex-direction: column; gap: 1px; flex: 1 1 auto; min-width: 0; }
    .name { font-weight: 500; }
    .pad { padding: 8px 2px; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class ActAsDialog {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly ref = inject(MatDialogRef<ActAsDialog>);

  readonly form = new FormSubmit();
  readonly loading = signal(true);
  readonly people = signal<ImpersonationTargetDto[]>([]);
  readonly search = signal('');

  readonly visible = computed(() => {
    const term = this.search().trim().toLowerCase();
    const all = this.people();
    if (!term) return all;
    return all.filter(
      (p) => p.displayName.toLowerCase().includes(term) || p.userName.toLowerCase().includes(term));
  });

  constructor() {
    this.api.impersonationTargets().subscribe({
      next: (people) => { this.people.set(people); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  actAs(person: ImpersonationTargetDto): void {
    this.ref.disableClose = true;

    this.form.run(
      (ctx: HttpContext) => this.auth.impersonate(person.id, ctx),
      () => {
        this.ref.disableClose = false;
        this.ref.close(true);
        // Home, not wherever they were: half the screens an administrator has open are ones the
        // person being acted as cannot reach, and landing on a 404 is a poor first impression of
        // the thing you are demonstrating.
        void this.router.navigateByUrl('/');
      },
    );
  }
}
