import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../../core/api.service';
import { AuthService } from '../../../core/auth.service';
import { ToastService } from '../../../core/toast.service';
import { Perm } from '../../../core/permissions';
import { DependencyType, TaskDependencyGraphDto, TaskSummaryDto } from '../../../core/models';
import { humanizeEnum } from '../../../core/format';
import { SearchSelectComponent, SelectOption } from '../../../shared/search-select.component';
import { ChipComponent, EmptyComponent } from '../../../shared/ui';

/**
 * Only DependsOn and Blocks impose an order — those are the ones that can hold work up and the
 * only ones the server cycle-checks. Related and Duplicate are cross-references. ParentChild is
 * absent on purpose: parentage lives on the Subtasks tab.
 */
@Component({
  selector: 'app-task-dependencies',
  standalone: true,
  imports: [
    RouterLink, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatTooltipModule, ChipComponent, EmptyComponent, SearchSelectComponent,
  ],
  template: `
    <div class="stack">
      @if (graph(); as g) {
        @if (g.isBlocked) {
          <div class="banner">
            <mat-icon>block</mat-icon>
            <span>Blocked by <strong>{{ g.blockedBy.join(', ') }}</strong> — the timer will refuse to start.</span>
          </div>
        }

        @if (canEdit) {
          <div class="card card-pad">
            <div class="row row-wrap">
              <mat-form-field class="grow">
                <mat-label>Find a task</mat-label>
                <input matInput [(ngModel)]="search" (keyup.enter)="find()"
                       placeholder="Task number or title" />
                <mat-icon matSuffix>search</mat-icon>
              </mat-form-field>

              <app-search-select class="type" label="Relationship" [options]="typeOptions"
                                 [(ngModel)]="type" />

              <button matButton (click)="find()">Search</button>
            </div>

            @for (candidate of results(); track candidate.id) {
              <div class="result">
                <span class="mono small muted">{{ candidate.taskNumber }}</span>
                <span class="truncate">{{ candidate.title }}</span>
                <span class="spacer"></span>
                <button matButton="filled" (click)="add(candidate)">Link</button>
              </div>
            }
          </div>
        }

        <div class="card">
          <div class="card-pad"><h2 class="card-title" style="margin:0">This task…</h2></div>
          @if (g.outgoing.length === 0) {
            <app-empty message="No links from this task" icon="link" />
          } @else {
            @for (dep of g.outgoing; track dep.id) {
              <div class="dep">
                <span class="chip" [class.tone-danger]="dep.isBlocking"
                      [class.tone-neutral]="!dep.isBlocking">{{ label(dep.type) }}</span>
                <a class="mono small" [routerLink]="['/tasks', dep.relatedTaskId]">
                  {{ dep.relatedTaskNumber }}
                </a>
                <span class="truncate">{{ dep.relatedTaskTitle }}</span>
                <span class="spacer"></span>
                <app-chip [value]="dep.relatedTaskStatus" kind="status" />
                @if (canEdit) {
                  <button matIconButton (click)="remove(dep.id)" matTooltip="Remove link">
                    <mat-icon>close</mat-icon>
                  </button>
                }
              </div>
            }
          }
        </div>

        @if (g.incoming.length > 0) {
          <div class="card">
            <div class="card-pad"><h2 class="card-title" style="margin:0">Other tasks…</h2></div>
            @for (dep of g.incoming; track dep.id) {
              <div class="dep">
                <a class="mono small" [routerLink]="['/tasks', dep.taskId]">{{ dep.relatedTaskNumber }}</a>
                <span class="chip tone-neutral">{{ label(dep.type) }} this</span>
                <span class="truncate">{{ dep.relatedTaskTitle }}</span>
                <span class="spacer"></span>
                <app-chip [value]="dep.relatedTaskStatus" kind="status" />
              </div>
            }
          </div>
        }
      }
    </div>
  `,
  styles: `
    .banner {
      display: flex; align-items: center; gap: 10px; padding: 12px 16px;
      border-radius: var(--radius); background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .grow { flex: 1 1 220px; margin-bottom: -1.25em; }
    .type { width: 240px; margin-bottom: -1.25em; }
    .result, .dep {
      display: flex; align-items: center; gap: 10px;
      padding: 9px 20px; border-top: 1px solid var(--border);
    }
    .result { padding-left: 0; padding-right: 0; }
    .dep:first-of-type { border-top: none; }
  `,
})
export class TaskDependenciesComponent implements OnInit {
  readonly taskId = input.required<number>();
  readonly changed = output<void>();

  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly graph = signal<TaskDependencyGraphDto | null>(null);
  readonly results = signal<TaskSummaryDto[]>([]);

  readonly canEdit = this.auth.has(Perm.taskAssign);
  search = '';
  type: DependencyType = 'DependsOn';

  readonly typeOptions: SelectOption[] = [
    { value: 'DependsOn', label: 'Depends on (they go first)' },
    { value: 'Blocks', label: 'Blocks (we go first)' },
    { value: 'Related', label: 'Related' },
    { value: 'Duplicate', label: 'Duplicate of' },
  ];

  label = (value: string) => humanizeEnum(value);

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.dependencies(this.taskId()).subscribe((g) => this.graph.set(g));
  }

  find(): void {
    if (!this.search.trim()) return;

    this.api.tasks({ search: this.search.trim(), openOnly: false, pageSize: 8 })
      .subscribe((page) => this.results.set(page.items.filter((t) => t.id !== this.taskId())));
  }

  add(candidate: TaskSummaryDto): void {
    this.api.addDependency(this.taskId(), candidate.id, this.type).subscribe((g) => {
      this.graph.set(g);
      this.results.set([]);
      this.search = '';
      this.toast.success(`Linked to ${candidate.taskNumber}.`);
      this.changed.emit();
    });
  }

  remove(dependencyId: number): void {
    this.api.removeDependency(this.taskId(), dependencyId).subscribe((g) => {
      this.graph.set(g);
      this.toast.success('Link removed.');
      this.changed.emit();
    });
  }
}
