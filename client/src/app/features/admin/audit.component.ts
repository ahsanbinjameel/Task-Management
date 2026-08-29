import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { PageEvent } from '@angular/material/paginator';
import { ApiService } from '../../core/api.service';
import { AuditLogDto, PagedResult } from '../../core/models';
import { PageHeaderComponent } from '../../shared/ui';
import { columnFilters } from '../../shared/column-filter.component';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';

@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [DatePipe, PageHeaderComponent, DataGridComponent, GridCellDirective],
  template: `
    <div class="page fills">
      <app-page-header title="Audit log" />

      <app-data-grid
        [rows]="page().items" [columns]="columns()"
        [loading]="loading()" [refreshing]="refreshing()" [filters]="filters"
        [total]="page().totalCount" [pageSize]="page().pageSize"
        [pageIndex]="page().page - 1" (pageChange)="onPage($event)"
        emptyMessage="No audit entries yet" emptyIcon="policy"
        noMatchesMessage="No audit entries match those filters.">

        <ng-template gridCell="when" let-a>
          <span class="nowrap">{{ a.createdAt | date: 'MMM d, y HH:mm:ss' }}</span>
        </ng-template>

        <ng-template gridCell="action" let-a>
          <span class="chip tone-neutral">{{ a.action }}</span>
        </ng-template>

        <ng-template gridCell="changes" let-a>
          <span class="changes">{{ a.newValues }}</span>
        </ng-template>
      </app-data-grid>
    </div>
  `,
  styles: `
    .changes {
      display: inline-block; max-width: 380px;
      overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
    }
  `,
})
export class AuditComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly page = signal<PagedResult<AuditLogDto>>(
    { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
  readonly actions = signal<string[]>([]);
  readonly loading = signal(true);
  readonly refreshing = signal(false);
  private loaded = false;

  /**
   * The filter row replaced a card above the grid holding an action dropdown, an entity-type box
   * and an entity-id box — three controls describing two of the columns below them.
   */
  readonly filters = columnFilters(() => { this.pageIndex = 0; this.reload(); });

  readonly columns = computed<GridColumn<AuditLogDto>[]>(() => [
    { key: 'when', header: 'When', cellClass: 'mono small', minWidth: 170 },
    {
      key: 'action', header: 'Action', minWidth: 190,
      filter: {
        kind: 'select', placeholder: 'Any action', singleOnly: true,
        options: this.actions().map((a) => ({ value: a, label: a })),
      },
    },
    { key: 'actor', header: 'Actor', cell: (a) => a.actorDisplayName },
    {
      // One box for both halves of the identity the endpoint understands: a type, or `#12` for a
      // particular row. Two controls for one column would be two chances to filter by half of it.
      key: 'entity', header: 'Entity', cellClass: 'mono small', minWidth: 150,
      cell: (a) => `${a.entityType}${a.entityId ? ' #' + a.entityId : ''}`,
      filter: { kind: 'text', placeholder: 'Type, or #12' },
    },
    { key: 'changes', header: 'Changes', cellClass: 'mono small', minWidth: 260 },
  ]);

  private pageIndex = 0;
  private pageSize = 50;

  ngOnInit(): void {
    this.reload();
    this.api.auditActions().subscribe({ next: (a) => this.actions.set(a), error: () => undefined });
  }

  reload(): void {
    if (this.loaded) this.refreshing.set(true); else this.loading.set(true);

    const entity = this.filters.value('entity').trim();
    const id = entity.startsWith('#') ? Number(entity.slice(1)) : NaN;

    this.api.audit({
      action: this.filters.value('action') || undefined,
      entityType: Number.isNaN(id) ? entity || undefined : undefined,
      entityId: Number.isNaN(id) ? undefined : id,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
    }).subscribe({
      next: (result) => { this.page.set(result); this.settle(); },
      error: () => this.settle(),
    });
  }

  private settle(): void {
    this.loaded = true;
    this.loading.set(false);
    this.refreshing.set(false);
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload();
  }
}
