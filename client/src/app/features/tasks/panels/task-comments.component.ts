import { Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../../core/api.service';
import { AuthService } from '../../../core/auth.service';
import { Perm } from '../../../core/permissions';
import { CommentCategory, TaskCommentDto } from '../../../core/models';
import { commentCategoryLabel } from '../../../core/labels';
import { enumOptions, SearchSelectComponent } from '../../../shared/search-select.component';
import { EmptyComponent } from '../../../shared/ui';

/** Customer-facing by default; everything else is internal unless deliberately shared. */
const VISIBLE_BY_DEFAULT: CommentCategory[] = [
  'RequesterCommunication', 'Clarification', 'ProgressUpdate', 'ResolutionNote',
];

@Component({
  selector: 'app-task-comments',
  standalone: true,
  imports: [
    DatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatSlideToggleModule, MatTooltipModule, EmptyComponent, SearchSelectComponent,
  ],
  template: `
    <div class="stack">
      <div class="card card-pad">
        <mat-form-field class="full">
          <mat-label>Add a comment</mat-label>
          <textarea matInput rows="3" [(ngModel)]="body"
                    placeholder="What should the next person to open this know?"></textarea>
        </mat-form-field>

        <div class="row row-wrap">
          <app-search-select class="category" label="Category" [options]="categoryOptions"
                             [(ngModel)]="category" (valueChange)="onCategoryChange()" />

          <mat-slide-toggle [(ngModel)]="visibleToRequester"
                            matTooltip="Whether the person who raised the request can read this">
            Visible to requester
          </mat-slide-toggle>

          <span class="spacer"></span>
          <button matButton="filled" [disabled]="!body.trim() || busy()" (click)="add()">
            Post
          </button>
        </div>
      </div>

      <div class="card">
        @if (comments().length === 0) {
          <app-empty message="No comments yet" icon="forum"
                     hint="Notes here are permanent — nothing is ever edited or deleted." />
        } @else {
          @for (comment of comments(); track comment.id) {
            <div class="comment">
              <div class="row">
                <strong class="small">{{ comment.authorDisplayName ?? 'Unknown' }}</strong>
                <span class="chip tone-muted">{{ label(comment.category) }}</span>
                @if (comment.visibleToRequester) {
                  <span class="chip tone-good" matTooltip="The requester can read this">
                    Requester-visible
                  </span>
                }
                <span class="spacer"></span>
                <span class="muted small">{{ comment.createdAt | date: 'MMM d, HH:mm' }}</span>
              </div>
              <p class="body">{{ comment.body }}</p>
            </div>
          }
        }
      </div>
    </div>
  `,
  styles: `
    .full { width: 100%; }
    .category { width: 220px; margin-bottom: -1.25em; }
    .comment { padding: 13px 20px; border-top: 1px solid var(--border); }
    .comment:first-child { border-top: none; }
    .body { margin: 7px 0 0; white-space: pre-wrap; font-size: 13.5px; line-height: 1.55; }
  `,
})
export class TaskCommentsComponent implements OnInit {
  readonly taskId = input.required<number>();

  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  readonly comments = signal<TaskCommentDto[]>([]);
  readonly busy = signal(false);

  body = '';
  category: CommentCategory = 'General';
  visibleToRequester = false;

  readonly categories: CommentCategory[] = [
    'General', 'RequesterCommunication', 'Clarification', 'InternalNote', 'TechnicalNote',
    'ProgressUpdate', 'QCNote', 'ResolutionNote',
    ...(this.auth.has(Perm.dashboardManagement) ? ['ManagementNote' as CommentCategory] : []),
  ];

  readonly categoryOptions = enumOptions(this.categories);

  label = (value: string) => commentCategoryLabel(value as CommentCategory);

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.comments(this.taskId()).subscribe((list) => this.comments.set(list));
  }

  /** Follows the server's default so the toggle shows what will actually happen. */
  onCategoryChange(): void {
    this.visibleToRequester = VISIBLE_BY_DEFAULT.includes(this.category);
  }

  add(): void {
    this.busy.set(true);
    this.api.addComment(this.taskId(), {
      body: this.body.trim(),
      category: this.category,
      visibleToRequester: this.visibleToRequester,
    }).subscribe({
      next: () => {
        this.body = '';
        this.busy.set(false);
        this.load();
      },
      error: () => this.busy.set(false),
    });
  }
}
