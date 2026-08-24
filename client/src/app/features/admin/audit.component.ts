import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { ApiService } from '../../core/api.service';
import { AuditLogDto, PagedResult } from '../../core/models';
import { SearchSelectComponent } from '../../shared/search-select.component';
import { EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';

@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [
    DatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatTableModule, MatPaginatorModule,
    PageHeaderComponent, EmptyComponent, LoadingComponent, SearchSelectComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Audit log"
                       subtitle="Append-only. Nothing here can be edited or deleted, by design." />

      <div class="card card-pad filters">
        <app-search-select label="Action" nullLabel="Any" [options]="actionOptions()"
                           [(ngModel)]="action" (valueChange)="reload()" />

        <mat-form-field>
          <mat-label>Entity type</mat-label>
          <input matInput [(ngModel)]="entityType" (keyup.enter)="reload()" placeholder="WorkTask" />
        </mat-form-field>

        <mat-form-field class="narrow-field">
          <mat-label>Entity id</mat-label>
          <input matInput type="number" [(ngModel)]="entityId" (keyup.enter)="reload()" />
        </mat-form-field>

        <span class="spacer"></span>
        <button matButton (click)="reload()"><mat-icon>refresh</mat-icon> Apply</button>
      </div>

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0) {
          <app-empty message="No audit entries match" icon="policy"
                     hint="Widen the date range, or clear the action filter." />
        } @else {
          <div class="table-scroll">
            <table mat-table [dataSource]="page().items">
              <ng-container matColumnDef="when">
                <th mat-header-cell *matHeaderCellDef>When</th>
                <td mat-cell *matCellDef="let a" class="mono small nowrap">
                  {{ a.createdAt | date: 'MMM d, y HH:mm:ss' }}
                </td>
              </ng-container>
              <ng-container matColumnDef="action">
                <th mat-header-cell *matHeaderCellDef>Action</th>
                <td mat-cell *matCellDef="let a"><span class="chip tone-neutral">{{ a.action }}</span></td>
              </ng-container>
              <ng-container matColumnDef="actor">
                <th mat-header-cell *matHeaderCellDef>Actor</th>
                <td mat-cell *matCellDef="let a">{{ a.actorDisplayName ?? '—' }}</td>
              </ng-container>
              <ng-container matColumnDef="entity">
                <th mat-header-cell *matHeaderCellDef>Entity</th>
                <td mat-cell *matCellDef="let a" class="mono small">
                  {{ a.entityType }}{{ a.entityId ? ' #' + a.entityId : '' }}
                </td>
              </ng-container>
              <ng-container matColumnDef="changes">
                <th mat-header-cell *matHeaderCellDef>Changes</th>
                <td mat-cell *matCellDef="let a" class="mono small changes">
                  {{ a.newValues }}
                </td>
              </ng-container>
              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns"></tr>
            </table>
          </div>
          <mat-paginator [length]="page().totalCount" [pageSize]="page().pageSize"
                         [pageIndex]="page().page - 1" [pageSizeOptions]="[25, 50, 100]"
                         (page)="onPage($event)" />
        }
      </div>
    </div>
  `,
  styles: `
    .filters { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; margin-bottom: 16px; }
    .filters mat-form-field { margin-bottom: -1.25em; }
    .filters app-search-select { width: 200px; margin-bottom: -1.25em; }
    .narrow-field { width: 120px; }
    .changes { max-width: 380px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  `,
})
export class AuditComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly page = signal<PagedResult<AuditLogDto>>(
    { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
  readonly actions = signal<string[]>([]);
  readonly actionOptions = computed(() => this.actions().map((a) => ({ value: a, label: a })));
  readonly loading = signal(true);
  readonly columns = ['when', 'action', 'actor', 'entity', 'changes'];

  action: string | null = null;
  entityType = '';
  entityId: number | null = null;

  private pageIndex = 0;
  private pageSize = 50;

  ngOnInit(): void {
    this.reload();
    this.api.auditActions().subscribe({ next: (a) => this.actions.set(a), error: () => undefined });
  }

  reload(): void {
    this.loading.set(true);
    this.api.audit({
      action: this.action ?? undefined,
      entityType: this.entityType || undefined,
      entityId: this.entityId ?? undefined,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
    }).subscribe({
      next: (result) => { this.page.set(result); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload();
  }
}
