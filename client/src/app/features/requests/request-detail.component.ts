import { DestroyRef, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { HttpContext } from '@angular/common/http';
import { RealtimeService, RequestChangedEvent } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { BreadcrumbsComponent, Crumb } from '../../shared/breadcrumbs.component';
import { BackLinkComponent } from '../../shared/back-link.component';
import { RequestEditDialog } from './request-edit-dialog.component';
import { FollowUpDialog } from './follow-up-dialog.component';
import { ConfirmDialog, ConfirmData, ReasonDialog, ReasonData } from '../../shared/dialogs';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { Perm } from '../../core/permissions';
import { saveBlob } from '../../core/format';
import { requestTypeLabel } from '../../core/labels';
import { Priority, RequestDetailDto, RequestType, TriageOutcome, TriageResultDto } from '../../core/models';
import { enumOptions, SearchSelectComponent, SelectOption } from '../../shared/search-select.component';
import { AttachmentsComponent } from '../../shared/attachments.component';
import {
  ChipComponent, FieldComponent, LoadingComponent, PageHeaderComponent,
} from '../../shared/ui';

/**
 * A request, and — for a reviewer — the triage panel.
 *
 * Triage is the gate between "someone asked" and "someone is doing it". Six of its seven outcomes
 * create no work at all, and only Approve produces a task. The panel makes that
 * asymmetry visible instead of burying it in a dropdown.
 */
@Component({
  selector: 'app-request-detail',
  standalone: true,
  imports: [
    BreadcrumbsComponent, BackLinkComponent,
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatMenuModule, MatTooltipModule,
    PageHeaderComponent, ChipComponent, FieldComponent, LoadingComponent,
    SearchSelectComponent, AttachmentsComponent,
  ],
  template: `
    @if (loading()) {
      <app-loading message="Loading request…" />
    } @else if (request(); as r) {
      <div class="page">
        <app-back-link fallback="/requests" label="Requests" />
        <app-breadcrumbs [crumbs]="crumbs(r)" />
        <app-page-header [title]="r.title" [subtitle]="r.requestNumber + ' · ' + label(r.type)">
          @if (canEdit()) {
            <button matButton (click)="edit(r)"><mat-icon>edit</mat-icon> Change this request</button>
          }
          <!--
            Offered once the request has been decided on — which is exactly when editing stops
            being allowed (PRODUCT-CORE §6). A point found while testing a fix is real and worth
            raising; what it must not do is quietly grow work that has already been committed and
            scheduled. So the two are offered in sequence: change it while nothing has been planned
            around it, raise a follow-up once something has.
          -->
          @if (canFollowUp()) {
            <button matButton (click)="followUp(r)">
              <mat-icon>add_comment</mat-icon> Found something else
            </button>
          }
          <!--
            The requester is never sent to the task. Their request is the record of it, and the
            progress panel below says everything the task screen would have told them. People who
            coordinate the work keep the link, because for them the task is the thing they act on.
          -->
          @if (r.generatedTaskId && canSeeTask()) {
            <a matButton [routerLink]="['/tasks', r.generatedTaskId]">
              <mat-icon>task_alt</mat-icon> View task
            </a>
          }
        </app-page-header>

        <div class="layout">
          <div class="stack">
            <!--
              What is happening, first and in plain words. This is the answer to the only question
              most requesters open the screen with, so it goes above the thing they already wrote.
            -->
            @if (r.progress; as p) {
              <div class="card card-pad progress-card">
                <div class="row row-wrap chips">
                  <span class="chip" [class]="'tone-' + tone(r.viewKey)">{{ r.viewLabel }}</span>
                  @if (p.progressPercent > 0) {
                    <span class="muted small">{{ p.progressPercent }}% done</span>
                  }
                </div>

                <div class="facts">
                  <div>
                    <span class="k">Responsible person</span>
                    <span class="v">{{ p.responsibleDisplayName ?? 'Not decided yet' }}</span>
                  </div>
                  @if (p.supportPeople.length) {
                    <div>
                      <span class="k">Support</span>
                      <span class="v">{{ p.supportPeople.join(', ') }}</span>
                    </div>
                  }
                  <div>
                    <span class="k">Quality check</span>
                    <span class="v">{{ p.qualityCheck }}</span>
                  </div>
                  @if (p.dueDate) {
                    <div>
                      <span class="k">Expected by</span>
                      <span class="v">{{ p.dueDate | date: 'mediumDate' }}</span>
                    </div>
                  }
                </div>

                @if (p.waitingReason) {
                  <p class="waiting">
                    <mat-icon>pause_circle</mat-icon>
                    <span>{{ p.waitingReason }}</span>
                  </p>
                }

                @if (p.latestUpdate) {
                  <div class="update">
                    <span class="k">Latest update</span>
                    <p class="body-text">{{ p.latestUpdate }}</p>
                    <span class="muted small">
                      {{ p.latestUpdateBy }} · {{ p.latestUpdateAt | date: 'MMM d, HH:mm' }}
                    </span>
                  </div>
                }

                <!--
                  The last hop of the relay (PRODUCT-CORE §7). Today this is the requester telling
                  Ahsan "haan hogya" on WhatsApp and Ahsan updating a sheet; here the person who
                  asked closes their own loop.

                  Offered only to the person who actually asked. The server enforces the same rule
                  on the record, so this is about not showing a button that would 403 — but it is
                  also the honest shape: nobody else is in a position to answer.
                -->
                @if (canConfirm(r)) {
                  <div class="confirm-panel">
                    <p class="confirm-ask">
                      <mat-icon>help_outline</mat-icon>
                      <span>
                        We think this is done. Please check it on your side and tell us —
                        is it fixed?
                      </span>
                    </p>
                    <div class="row row-wrap">
                      <button matButton="filled" [disabled]="busy()" (click)="acceptFix(r)">
                        <mat-icon>check_circle</mat-icon> It's fixed
                      </button>
                      <button matButton [disabled]="busy()" (click)="rejectFix(r)">
                        <mat-icon>replay</mat-icon> Still not fixed
                      </button>
                    </div>
                  </div>
                }
              </div>
            }

            <div class="card card-pad">
              <div class="row row-wrap chips">
                @if (!r.progress) {
                  <span class="chip" [class]="'tone-' + tone(r.viewKey)">{{ r.viewLabel }}</span>
                }
                <app-chip [value]="r.requestedUrgency" kind="urgency" />
                @if (r.round > 1 && r.relatedRequestId) {
                  <a class="chip tone-warn round" [routerLink]="['/requests', r.relatedRequestId]"
                     matTooltip="Found in a later round of testing. It did not change the deadline of
                                 the work it came out of.">
                    Round {{ r.round }} · from {{ r.relatedRequestNumber }}
                  </a>
                }
                @if (r.productLocation) {
                  <span class="muted small">{{ r.productLocation }}</span>
                }
                <span class="muted small">
                  Requested by {{ r.requestedByDisplayName }}
                  on {{ r.requestedAt | date: 'mediumDate' }}
                </span>
              </div>

              <h2 class="card-title top-gap">Description</h2>
              <p class="body-text">{{ r.description }}</p>

              @if (r.businessImpact) {
                <h2 class="card-title top-gap">Business impact</h2>
                <p class="body-text">{{ r.businessImpact }}</p>
              }
              @if (r.expectedResult) {
                <h2 class="card-title top-gap">Expected result</h2>
                <p class="body-text">{{ r.expectedResult }}</p>
              }
              @if (r.currentResult) {
                <h2 class="card-title top-gap">What happens instead</h2>
                <p class="body-text">{{ r.currentResult }}</p>
              }
              @if (r.reproductionSteps) {
                <h2 class="card-title top-gap">Steps to reproduce</h2>
                <p class="body-text">{{ r.reproductionSteps }}</p>
              }
            </div>

            <!--
              Questions & Replies. "Clarifications" is the workflow's word for it, not a word
              anyone uses; and an empty one gets a single line rather than a card of empty space —
              a section with nothing in it should take up nothing.
            -->
            @if (r.clarifications.length === 0) {
              <!-- Nothing to show costs one line, not a card of empty space. -->
              <p class="empty-line">
                <span class="k">Questions &amp; replies</span><span class="v muted">None</span>
              </p>
            } @else {
            <div class="card">
              <div class="card-pad">
                <h2 class="card-title" style="margin:0">Questions &amp; Replies</h2>
              </div>
              @if (r.clarifications.length > 0) {
                @for (c of r.clarifications; track c.id) {
                  <div class="clarification">
                    <div class="q">
                      <mat-icon>help</mat-icon>
                      <div>
                        <p class="body-text">{{ c.question }}</p>
                        <span class="muted small">{{ c.askedAt | date: 'MMM d, HH:mm' }}</span>
                      </div>
                    </div>

                    @if (c.answer) {
                      <div class="a">
                        <mat-icon>reply</mat-icon>
                        <div>
                          <p class="body-text">{{ c.answer }}</p>
                          <span class="muted small">{{ c.answeredAt | date: 'MMM d, HH:mm' }}</span>
                        </div>
                      </div>
                    } @else if (isRequester()) {
                      <div class="answer-box">
                        <mat-form-field class="full">
                          <mat-label>Your answer</mat-label>
                          <textarea matInput rows="2" [(ngModel)]="answers[c.id]"></textarea>
                        </mat-form-field>
                        <button matButton="filled" [disabled]="!answers[c.id]?.trim()"
                                (click)="answer(c.id)">Send answer</button>
                      </div>
                    } @else {
                      <p class="muted small pending">Waiting on the requester.</p>
                    }
                  </div>
                }
              }
            </div>
            }

            <!-- Screenshots shown as screenshots: looking at one should not mean downloading it. -->
            @if (r.attachments.length === 0) {
              <p class="empty-line">
                <span class="k">Attachments</span><span class="v muted">None</span>
                <span class="spacer"></span>
                <button matButton class="tight" (click)="file.click()">
                  <mat-icon>upload</mat-icon> Add a file
                </button>
                <input #file type="file" hidden (change)="upload($event)" />
              </p>
            } @else {
              <div class="card card-pad">
                <div class="row" style="margin-bottom:12px">
                  <h2 class="card-title" style="margin:0">Attachments</h2>
                  <span class="spacer"></span>
                  <button matButton (click)="file2.click()">
                    <mat-icon>upload</mat-icon> Add a file
                  </button>
                  <input #file2 type="file" hidden (change)="upload($event)" />
                </div>
                <app-attachments [attachments]="r.attachments" />
              </div>
            }

            <!-- --- history ------------------------------------------------------------------ -->
            @if (r.activity.length > 0) {
              <div class="card">
                <div class="card-pad"><h2 class="card-title" style="margin:0">History</h2></div>
                @for (a of r.activity; track a.id) {
                  <div class="event">
                    <mat-icon>history</mat-icon>
                    <div class="what">
                      <div>{{ a.description }}</div>
                      <div class="muted small">
                        {{ a.actorDisplayName }} · {{ a.occurredAt | date: 'MMM d, HH:mm' }}
                      </div>
                    </div>
                  </div>
                }
              </div>
            }
          </div>

          <!-- --- triage ---------------------------------------------------------------------- -->
          <aside class="stack">
            @if (canTriage()) {
              <div class="card card-pad">
                <h2 class="card-title">Your decision</h2>

                <!--
                  The four decisions that get made, two across, plus the rarer three behind a menu.
                  Seven full-width rows was most of the height of this panel, and the reader had to
                  read all seven every time to find the one they wanted — which is nearly always
                  Approve.
                -->
                <div class="outcomes">
                  @for (o of primaryOutcomes(); track o.value) {
                    <button type="button" class="outcome" [class.on]="outcome === o.value"
                            (click)="outcome = o.value">
                      <mat-icon>{{ o.icon }}</mat-icon> {{ o.label }}
                    </button>
                  }
                </div>

                <div class="more-row">
                  @if (isSecondary(outcome)) {
                    <button type="button" class="outcome on wide" [matMenuTriggerFor]="moreMenu">
                      <mat-icon>{{ iconFor(outcome) }}</mat-icon> {{ labelFor(outcome) }}
                      <mat-icon class="caret">expand_more</mat-icon>
                    </button>
                  } @else {
                    <button type="button" class="more" [matMenuTriggerFor]="moreMenu">
                      Something else <mat-icon class="caret">expand_more</mat-icon>
                    </button>
                  }

                  <mat-menu #moreMenu="matMenu">
                    @for (o of secondaryOutcomes(); track o.value) {
                      <button mat-menu-item type="button" (click)="outcome = o.value">
                        <mat-icon>{{ o.icon }}</mat-icon><span>{{ o.label }}</span>
                      </button>
                    }
                  </mat-menu>
                </div>

                <!--
                  Only what the chosen decision needs. Approval fields left standing while somebody
                  rejects a request are three controls they have to read past to reach the one that
                  matters, and a tall panel for a decision that needed one sentence.
                -->
                @if (outcome === 'Approve') {
                  <div class="approve-fields">
                    <!--
                      Kind is set here rather than at intake. The requester knows what is wrong,
                      not whether it is a defect, a change request or a configuration mistake, and
                      a guess from them is a wrong label on every report that groups by it.
                    -->
                    <div class="pair">
                      <app-search-select label="Kind" [options]="typeOptions" [(ngModel)]="type"
                                         name="type" />
                      <app-search-select label="Priority" [options]="priorityOptions"
                                         [(ngModel)]="priority" name="priority" />
                    </div>

                    <mat-form-field class="full">
                      <mat-label>Estimate (hours)</mat-label>
                      <input matInput type="number" min="0" [(ngModel)]="estimate" name="estimate" />
                    </mat-form-field>

                    <mat-form-field class="full">
                      <mat-label>Acceptance criteria — one per line</mat-label>
                      <textarea matInput rows="3" [(ngModel)]="criteria" name="criteria"></textarea>
                    </mat-form-field>
                  </div>
                }

                @if (outcome === 'SendForVerification') {
                  <div class="verify-fields">
                    <app-search-select class="full" label="Give it to"
                                       nullLabel="Leave for someone to pick up"
                                       [options]="checkerOptions()" [(ngModel)]="checkerId"
                                       name="checker" />

                    <mat-form-field class="full">
                      <mat-label>What should they look at?</mat-label>
                      <textarea matInput rows="2" [(ngModel)]="verifyInstructions"
                                name="instructions"></textarea>
                    </mat-form-field>

                    <p class="note small">
                      This creates no task. Whatever they find, the request comes back to you.
                    </p>
                  </div>
                }

                @if (outcome === 'MarkDuplicate') {
                  <mat-form-field class="full">
                    <mat-label>Duplicate of (request id)</mat-label>
                    <input matInput type="number" [(ngModel)]="duplicateOf" name="duplicate" />
                  </mat-form-field>
                }

                @if (outcome !== 'Approve') {
                  <mat-form-field class="full">
                    <mat-label>{{ outcome === 'RequestClarification' ? 'What do you need to know?' : 'Reason' }}</mat-label>
                    <textarea matInput rows="3" [(ngModel)]="reason" name="reason"></textarea>
                  </mat-form-field>
                }

                <button matButton="filled" class="full submit"
                        [disabled]="!triageValid() || busy()" (click)="triage()">
                  {{ submitLabel() }}
                </button>
              </div>
            }

            @if (r.verifications.length > 0 && !isRequesterView()) {
              <div class="card card-pad">
                <h2 class="card-title">Checks</h2>
                @for (v of r.verifications; track v.id) {
                  <div class="check">
                    <div class="check-head">
                      <a [routerLink]="['/verifications', v.id]">{{ v.verificationNumber }}</a>
                      <span class="chip tone-neutral">{{ v.statusLabel }}</span>
                    </div>
                    <div class="muted small">
                      {{ v.assignedToDisplayName ?? 'Nobody yet' }}
                    </div>
                    @if (v.resultLabel) {
                      <div class="check-result"><strong>{{ v.resultLabel }}</strong></div>
                      <p class="check-findings">{{ v.findings }}</p>
                    }
                  </div>
                }
              </div>
            }

            <div class="card card-pad">
              <h2 class="card-title">Details</h2>
              @if (r.clientName) {
                <app-field label="Client">
                  {{ r.clientName }}
                </app-field>
              }
              <app-field label="Type">{{ label(r.type) }}</app-field>
              <!--
                Shown to everyone, requester included. Unlike "Generated task" this is not the
                system talking about itself: they asked for eight things in one go, and this is how
                they get back to the other seven.
              -->
              @if (r.batchNumber) {
                <app-field label="Asked for with">
                  <a [routerLink]="['/requests/batches', r.batchId]">
                    {{ r.batchNumber }} — item {{ r.ordinalInBatch }} of {{ r.batchItemCount }}
                  </a>
                </app-field>
              }
              <app-field label="Needed by">
                {{ r.targetDate ? (r.targetDate | date: 'mediumDate') : '—' }}
              </app-field>
              <!--
                The internal record, for the people who work on it. A requester is not shown it at
                all: "Generated task" is the system talking about itself, and the progress panel
                already answered the question that link would have been clicked to answer.
              -->
              @if (canSeeTask()) {
                <app-field label="Generated task">
                  @if (r.generatedTaskId) {
                    <a [routerLink]="['/tasks', r.generatedTaskId]">View task</a>
                  } @else { Not approved yet }
                </app-field>
              }
            </div>
          </aside>
        </div>
      </div>
    }
  `,
  styles: `
    .event {
      display: flex; gap: 10px; align-items: flex-start;
      padding: 11px 20px; border-top: 1px solid var(--border);
    }
    .event mat-icon { font-size: 18px; width: 18px; height: 18px; color: var(--text-muted); }
    .event .what { min-width: 0; }
    /* Wider than the old 340px rail. The decision is the reason a reviewer opened this page, and
       squeezing it into a narrow column is what forced its fields into a tall stack while the left
       side sat half empty. */
    .layout { display: grid; gap: 18px; grid-template-columns: minmax(0, 1fr) minmax(320px, 400px); }
    @media (max-width: 1150px) { .layout { grid-template-columns: 1fr; } }

    /* The decision panel follows the page rather than scrolling away from it on a long request. */
    @media (min-width: 1151px) {
      .layout > aside > .card:first-child { position: sticky; top: 12px; }
    }
    .chips { gap: 9px; }
    .top-gap { margin-top: 18px; }
    .body-text { margin: 0; white-space: pre-wrap; line-height: 1.55; font-size: 14px; }
    .clarification { padding: 14px 20px; border-top: 1px solid var(--border); }
    .clarification:first-of-type { border-top: none; }
    .q, .a { display: flex; gap: 10px; padding: 4px 0; }
    .a { padding-left: 26px; }
    .q mat-icon { color: var(--tone-warn-fg); font-size: 18px; width: 18px; height: 18px; }
    .a mat-icon { color: var(--tone-good-fg); font-size: 18px; width: 18px; height: 18px; }
    .answer-box { padding: 8px 0 0 26px; }
    .pending { padding-left: 28px; }
    .attachment {
      display: flex; align-items: center; flex-wrap: wrap; gap: 10px;
      padding: 9px 20px; border-top: 1px solid var(--border);
    }
    .attachment mat-icon { color: var(--text-muted); }
    .full { width: 100%; }
    .progress-card { border-left: 3px solid #1d69d4; }
    .round { text-decoration: none; }
    .confirm-panel {
      margin-top: 14px; padding-top: 14px; border-top: 1px solid var(--border);
    }
    .confirm-ask {
      display: flex; gap: 8px; align-items: flex-start;
      margin: 0 0 10px; font-size: 13.5px;
    }
    .confirm-ask mat-icon {
      color: var(--tone-warn-fg); font-size: 18px; width: 18px; height: 18px; flex: none;
    }
    .facts {
      display: grid; gap: 10px 24px; margin-top: 14px;
      grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
    }
    .facts .k, .update .k {
      display: block; font-size: 11.5px; text-transform: uppercase;
      letter-spacing: 0.04em; color: var(--text-muted); margin-bottom: 2px;
    }
    .facts .v { font-size: 14px; }
    .waiting {
      display: flex; align-items: flex-start; gap: 8px; margin: 14px 0 0;
      padding: 9px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-warn-bg); color: var(--tone-warn-fg);
    }
    .waiting mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    .update { margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border); }
    .update p { margin: 0 0 4px; }
    .note {
      margin: 0 0 12px; padding: 9px 11px; border-radius: 8px;
      background: var(--tone-running-bg); font-size: 12.5px; line-height: 1.5;
    }
    .check { padding: 10px 0; border-top: 1px solid var(--border); }
    .check:first-of-type { border-top: none; padding-top: 0; }
    .check-head { display: flex; align-items: center; gap: 8px; justify-content: space-between; }
    .check-result { margin-top: 6px; font-size: 13px; }
    .check-findings { margin: 4px 0 0; font-size: 12.5px; line-height: 1.5; white-space: pre-wrap; }
    /* Two across. Seven full-width rows was most of this panel's height, for a choice that is
       Approve nine times in ten. */
    .outcomes { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; margin-bottom: 6px; }
    .outcome {
      display: flex; align-items: center; justify-content: center; gap: 6px;
      padding: 9px 10px; border-radius: 8px; cursor: pointer;
      border: 1px solid var(--border); background: var(--surface);
      color: var(--text); font-size: 13px; font-weight: 500;
    }
    .outcome:hover { background: var(--surface-sunken); }
    .outcome.on { border-color: #1d69d4; background: var(--tone-running-bg); color: var(--tone-running-fg); }
    .outcome mat-icon { font-size: 17px; width: 17px; height: 17px; }
    .outcome .caret { margin-left: auto; opacity: .7; }
    .outcome.wide { width: 100%; justify-content: flex-start; }

    .more-row { margin-bottom: 12px; }
    .more {
      display: flex; align-items: center; gap: 4px; width: 100%;
      padding: 7px 10px; border-radius: 8px; cursor: pointer;
      border: 1px dashed var(--border-strong); background: transparent;
      color: var(--text-muted); font-size: 12.5px;
    }
    .more:hover { border-style: solid; color: var(--text); }
    .more .caret { margin-left: auto; font-size: 17px; width: 17px; height: 17px; }

    .approve-fields { display: flex; flex-direction: column; gap: 12px; }
    .approve-fields .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .approve-fields mat-form-field, .approve-fields app-search-select { width: 100%; }
    .verify-fields { display: flex; flex-direction: column; gap: 12px; }

    /* An empty section costs one line, not a card. */
    .empty-line {
      display: flex; align-items: center; gap: 10px; flex-wrap: wrap;
      margin: 0 0 10px; padding: 8px 2px; border-bottom: 1px solid var(--border);
      font-size: 13px;
    }
    .empty-line .k { font-weight: 600; }
    .empty-line .spacer { flex: 1 1 auto; }
    .empty-line .tight { min-width: 0; }
    .submit { margin-top: 6px; }
  `,
})
export class RequestDetailComponent implements OnInit {
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  readonly id = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly request = signal<RequestDetailDto | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);

  readonly priorities: Priority[] = ['Critical', 'High', 'Normal', 'Low'];
  readonly priorityOptions = enumOptions(this.priorities);
  readonly answers: Record<number, string> = {};

  outcome: TriageOutcome = 'Approve';
  checkerId: number | null = null;
  verifyInstructions = '';
  priority: Priority = 'Normal';
  estimate: number | null = null;
  criteria = '';

  /** Set by the reviewer, not the requester — see the note in the template. */
  type: RequestType = 'Bug';

  readonly typeOptions = enumOptions<RequestType>([
    'Bug', 'ChangeRequest', 'NewFeature', 'Support', 'Configuration',
    'Database', 'Report', 'Investigation', 'DataCorrection', 'Infrastructure', 'Other',
  ]);

  /**
   * The decisions worth putting in front of somebody, and the rarer ones behind a menu.
   *
   * Approve is what nearly every request gets; clarify, check and reject are the real
   * alternatives. Duplicate, defer and escalate are all genuine outcomes and all uncommon, and
   * seven equal-weight rows made the reader read past six to reach the one they wanted.
   */
  readonly primaryOutcomes = computed(() => {
    const outcomes: { value: TriageOutcome; label: string; icon: string }[] = [
      { value: 'Approve', label: 'Approve', icon: 'check_circle' },
      { value: 'RequestClarification', label: 'Clarification', icon: 'help' },
    ];

    if (this.canSendForVerification()) {
      outcomes.push({ value: 'SendForVerification', label: 'Check', icon: 'fact_check' });
    }

    outcomes.push({ value: 'Reject', label: 'Reject', icon: 'cancel' });
    return outcomes;
  });

  readonly secondaryOutcomes = computed(() => {
    const outcomes: { value: TriageOutcome; label: string; icon: string }[] = [
      { value: 'MarkDuplicate', label: 'Duplicate', icon: 'content_copy' },
      { value: 'Defer', label: 'Defer', icon: 'schedule' },
      { value: 'Escalate', label: 'Escalate', icon: 'priority_high' },
    ];

    // Checking only fits in the menu when it did not earn a place above.
    if (!this.canSendForVerification()) return outcomes;
    return outcomes;
  });

  isSecondary(outcome: TriageOutcome): boolean {
    return this.secondaryOutcomes().some((o) => o.value === outcome);
  }

  labelFor(outcome: TriageOutcome): string {
    return [...this.primaryOutcomes(), ...this.secondaryOutcomes()]
      .find((o) => o.value === outcome)?.label ?? 'Something else';
  }

  iconFor(outcome: TriageOutcome): string {
    return [...this.primaryOutcomes(), ...this.secondaryOutcomes()]
      .find((o) => o.value === outcome)?.icon ?? 'more_horiz';
  }
  reason = '';
  duplicateOf: number | null = null;

  private requestId = 0;

  /**
   * The server is the authority on whether an edit is allowed — it refuses once triage has acted.
   * This mirrors the same rule so the button is not offered when it would only produce a refusal.
   */
  readonly canEdit = computed(() => {
    const r = this.request();
    return !!r && this.isRequester()
      && (r.status === 'Submitted' || r.status === 'ClarificationRequired');
  });

  edit(request: RequestDetailDto): void {
    this.dialog
      // Material caps the dialog surface at 560px unless it is given a width, and this form is
      // deliberately wider than that. Without it the content overflows the panel and the fields
      // are clipped behind a sideways scrollbar.
      .open(RequestEditDialog, { data: { request }, width: 'min(680px, 92vw)', maxWidth: '92vw' })
      .afterClosed()
      .subscribe((updated?: RequestDetailDto) => {
        // Already saved by the dialog, which stayed open if it had failed.
        if (!updated) return;
        this.request.set(updated);
        this.toast.success('Your changes have been saved and the reviewers told.');
      });
  }

  crumbs(r: RequestDetailDto): Crumb[] {
    const trail: Crumb[] = [{ label: 'Requests', route: '/requests' }, { label: r.requestNumber }];
    return trail;
  }

  readonly isRequester = computed(() =>
    this.request()?.requestedByUserId === this.auth.user()?.id);

  /**
   * Whether to offer the two confirmation buttons: the work is waiting on a human answer, and the
   * reader is the human who has to give it. `confirm` is the server's own view key, so this cannot
   * drift from the statuses the tile counts.
   */
  /**
   * A follow-up is for a point found after this request was decided on — which is precisely when
   * editing stops being offered. Before that, changing the request itself is the honest move and a
   * second linked request would just be clutter.
   */
  readonly canFollowUp = computed(() => {
    const r = this.request();
    return !!r && this.auth.has(Perm.requestCreate) && !this.canEdit();
  });

  followUp(request: RequestDetailDto): void {
    this.dialog
      .open(FollowUpDialog, {
        data: {
          requestId: request.id,
          requestNumber: request.requestNumber,
          clientName: request.clientName,
          productLocation: request.productLocation,
          round: request.round,
        },
      })
      .afterClosed()
      .subscribe((created?: RequestDetailDto) => {
        // Already saved by the dialog, which stayed open if it had failed.
        if (!created) return;
        this.toast.success(`${created.requestNumber} raised, linked to ${request.requestNumber}.`);
        void this.router.navigate(['/requests', created.id]);
      });
  }

  canConfirm(request: RequestDetailDto): boolean {
    return request.viewKey === 'confirm' && this.isRequester();
  }

  acceptFix(request: RequestDetailDto): void {
    // Confirmed work closes, and closing cannot be taken back by the person doing it — so this
    // earns a dialog under the rule in CLAUDE.md. The note is optional: they have already said the
    // only thing that matters by pressing the button.
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: 'Is this fixed?',
          message:
            'This closes the request. If anything is still wrong, choose "Still not fixed" '
            + 'instead and tell us what you are seeing.',
          confirmText: "Yes, it's fixed",
          submit: (ctx: HttpContext) => this.api.acceptFix(request.generatedTaskId!, undefined, ctx),
        },
      })
      .afterClosed()
      .subscribe((done?: unknown) => {
        if (!done) return;
        this.toast.success('Thank you — this request is now closed.');
        this.load();
      });
  }

  rejectFix(request: RequestDetailDto): void {
    // The reason is mandatory here and the server agrees: "still broken" with no detail costs the
    // worker exactly the round-trip this screen exists to remove.
    this.dialog
      .open<ReasonDialog, ReasonData>(ReasonDialog, {
        data: {
          title: 'What is still not working?',
          message:
            'This goes back to the person who did the work, and it will be checked again '
            + 'before you are asked to confirm a second time.',
          label: 'What you are seeing',
          required: true,
          confirmText: 'Send it back',
          submit: (reason: string, ctx: HttpContext) =>
            this.api.rejectFix(request.generatedTaskId!, reason, ctx),
        },
      })
      .afterClosed()
      .subscribe((done?: unknown) => {
        if (!done) return;
        this.toast.success('Sent back — we will look at it again.');
        this.load();
      });
  }

  /** Triage only makes sense while the request is still open for a decision. */
  readonly canTriage = computed(() => {
    const r = this.request();
    return !!r && this.auth.has(Perm.taskReview)
      && ['Submitted', 'InReview', 'ClarificationRequired'].includes(r.status);
  });

  /**
   * The extra outcome, offered only to someone who may actually raise a check. Hiding it is a
   * courtesy — the endpoint refuses it either way — but offering a button that always 403s is
   * worse than not offering it.
   */
  readonly canSendForVerification = computed(() => this.auth.has(Perm.verificationCreate));

  /**
   * Whether this reader is being shown the plain-language view. Checks are internal vocabulary:
   * a requester is told "Being Checked" on their status and nothing more, because "VER-000012 is
   * with Quentin" is our terminology, not theirs.
   */
  readonly isRequesterView = computed(() =>
    !this.auth.hasAny(Perm.taskReview, Perm.taskAssign, Perm.verificationViewAll));

  /** Who a check can be given to. Loaded only for someone who can raise one. */
  readonly checkerOptions = signal<SelectOption[]>([]);

  label = (value: string) => requestTypeLabel(value as RequestType);

  /**
   * Colour follows what the view means, not the internal status name — the reader is being shown
   * "Being Checked", so the chip has to be coloured on that basis.
   */
  tone(viewKey: string): string {
    switch (viewKey) {
      case 'done': return 'success';
      case 'declined': return 'danger';
      // Amber, like "Needs Your Input": both mean the request is waiting on the person reading it.
      case 'confirm': case 'input': case 'waiting': return 'warn';
      case 'working': case 'checking': return 'running';
      default: return 'neutral';
    }
  }

  /** Only people who act on tasks are offered the task. See the note in the template. */
  canSeeTask = () => this.auth.has(Perm.taskAssign) || this.auth.has(Perm.taskWork)
    || this.auth.has(Perm.taskReview) || this.auth.has(Perm.taskQCReview);

  ngOnInit(): void {
    this.requestId = Number(this.id());
    this.load();

    if (this.canSendForVerification()) {
      this.api.assignableCheckers().subscribe((checkers) => {
        this.checkerOptions.set(checkers.map((c) => ({
          value: c.userId,
          label: c.displayName,
          hint: c.openVerifications === 0 ? 'free' : `${c.openVerifications} open`,
        })));
      });
    }
  
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn<RequestChangedEvent>(
      [this.realtime.requestChanged],
      () => this.load(),
      this.destroyRef,
      { filter: (e) => e.requestId === this.requestId });
  }

  private load(): void {
    this.api.request(this.requestId).subscribe({
      next: (r) => {
        this.request.set(r);
        this.priority = (r.requestedUrgency as unknown as Priority) ?? 'Normal';
        // Start from whatever the request already has, so a reviewer confirming a sensible default
        // does not have to pick it again.
        this.type = r.type;
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  triageValid(): boolean {
    if (this.outcome === 'Approve') return true;
    if (this.outcome === 'MarkDuplicate' && !this.duplicateOf) return false;
    // Sending for checking needs no reason: the instructions are the reason, and they are optional
    // because the request the checker is looking at already says what the problem is supposed to be.
    if (this.outcome === 'SendForVerification') return true;
    return this.reason.trim().length > 0;
  }

  submitLabel(): string {
    switch (this.outcome) {
      case 'Approve': return 'Approve and create task';
      case 'SendForVerification': return 'Send for checking';
      default: return 'Record decision';
    }
  }

  /**
   * The wording for the three decisions that cannot be taken back — null for the ones that can.
   *
   * Approve is the gate the whole system is built around: it creates the task, and there is no
   * un-approve. Reject and Duplicate end the request in a state triage will not offer again. The
   * other three (clarification, defer, escalate) leave the request live and re-decidable, so they
   * submit straight away — asking every time would train the reviewer to click through the one
   * question that matters.
   */
  private triageConfirmation(): { title: string; message: string; confirmText: string; danger?: boolean } | null {
    switch (this.outcome) {
      case 'Approve':
        return {
          title: 'Approve and create the task?',
          message:
            'This creates the task and puts the work into the queue. A request is never approved '
            + 'twice, so this cannot be undone — an unwanted task has to be closed or cancelled '
            + 'on its own page.',
          confirmText: 'Approve and create task',
        };
      case 'Reject':
        return {
          title: 'Reject this request?',
          message:
            'The requester is told, with your reason. The request is finished and cannot be put '
            + 'back into review — they would have to raise it again.',
          confirmText: 'Reject it',
          danger: true,
        };
      case 'MarkDuplicate':
        return {
          title: 'Close this as a duplicate?',
          message:
            'The request is finished and points at the other one instead. It cannot be put back '
            + 'into review.',
          confirmText: 'Mark duplicate',
          danger: true,
        };
      default:
        return null;
    }
  }

  triage(): void {
    const confirmation = this.triageConfirmation();

    if (!confirmation) {
      this.busy.set(true);
      this.submitTriage().subscribe({
        next: (result) => { this.busy.set(false); this.afterTriage(result); },
        error: () => this.busy.set(false),
      });
      return;
    }

    // The dialog performs the call itself, so a server refusal leaves the decision — and the
    // reason typed against it — exactly where the reviewer left it instead of clearing the panel.
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: { ...confirmation, submit: (ctx: HttpContext) => this.submitTriage(ctx) },
      })
      .afterClosed()
      .subscribe((result?: unknown) => {
        if (!result) return;
        this.afterTriage(result as TriageResultDto);
      });
  }

  private submitTriage(context?: HttpContext) {
    return this.api.triage(this.requestId, {
      outcome: this.outcome,
      reason: this.reason.trim() || undefined,
      approvedPriority: this.outcome === 'Approve' ? this.priority : undefined,
      estimatedEffortHours: this.outcome === 'Approve' ? (this.estimate ?? undefined) : undefined,
      acceptanceCriteria: this.outcome === 'Approve' ? (this.criteria.trim() || undefined) : undefined,
      // Only when approving: classifying work that may never be built is effort spent on nothing.
      type: this.outcome === 'Approve' ? this.type : undefined,
      duplicateOfRequestId: this.duplicateOf ?? undefined,
      verification: this.outcome === 'SendForVerification'
        ? {
            // The target is the request itself: that is what the reviewer could not decide about.
            targetType: 'Request' as const,
            instructions: this.verifyInstructions.trim() || null,
            assignToUserId: this.checkerId,
          }
        : undefined,
    }, context);
  }

  private afterTriage(result: TriageResultDto): void {
    this.reason = '';
    this.verifyInstructions = '';

    if (result.createdTaskId) {
      this.toast.success('Approved — the task has been created.');
      void this.router.navigate(['/tasks', result.createdTaskId]);
      return;
    }

    if (result.verificationId) {
      this.toast.success(`Sent for checking as ${result.verificationNumber}.`);
      void this.router.navigate(['/verifications', result.verificationId]);
      return;
    }

    // The decision is not the request: re-fetch it rather than assigning the response over
    // the top, which is what left the page rendering undefined.
    this.load();
    this.toast.success('Decision saved.');
  }

  answer(clarificationId: number): void {
    const text = this.answers[clarificationId]?.trim();
    if (!text) return;

    this.api.answerClarification(clarificationId, text).subscribe((updated) => {
      this.request.set(updated);
      this.answers[clarificationId] = '';
      this.toast.success('Answer sent — the request is back with the reviewer.');
    });
  }

  upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.api.uploadRequestAttachment(this.requestId, file).subscribe(() => {
      input.value = '';
      this.toast.success('File attached.');
      this.load();
    });
  }

  download(attachmentId: number, fileName: string): void {
    this.api.downloadAttachment(attachmentId).subscribe((blob) => saveBlob(blob, fileName));
  }

  size(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}
