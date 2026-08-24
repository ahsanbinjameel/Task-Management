import { Component, OnInit, inject, signal } from '@angular/core';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { RoleDto } from '../../core/models';
import { roleLabel } from '../../core/labels';
import { LoadingComponent, PageHeaderComponent } from '../../shared/ui';

/**
 * Roles are bundles of permissions, nothing more — authorization is permission-based throughout,
 * so this screen is a readable map of who can do what rather than an editor of behaviour.
 */
@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [MatExpansionModule, MatIconModule, PageHeaderComponent, LoadingComponent],
  template: `
    <div class="page narrow">
      <app-page-header title="Roles"
                       subtitle="Roles are bundles of permissions. Every check server-side is on a permission, never a role." />

      @if (loading()) {
        <app-loading />
      } @else {
        <mat-accordion multi>
          @for (role of roles(); track role.id) {
            <mat-expansion-panel>
              <mat-expansion-panel-header>
                <mat-panel-title>
                  <strong>{{ roleLabel(role.name) }}</strong>
                  @if (role.isSystemRole) { <span class="chip tone-muted sys">System</span> }
                </mat-panel-title>
                <mat-panel-description>
                  {{ role.permissions.length }} permissions
                </mat-panel-description>
              </mat-expansion-panel-header>

              @if (role.description) { <p class="muted small">{{ role.description }}</p> }

              <div class="perms">
                @for (permission of role.permissions; track permission) {
                  <span class="chip tone-neutral mono">{{ permission }}</span>
                } @empty {
                  <span class="muted small">No permissions granted.</span>
                }
              </div>
            </mat-expansion-panel>
          }
        </mat-accordion>
      }
    </div>
  `,
  styles: `
    .narrow { max-width: 900px; }
    .sys { margin-left: 9px; }
    .perms { display: flex; flex-wrap: wrap; gap: 6px; }
  `,
})
export class RolesComponent implements OnInit {
  /** The wording layer — `AssignmentManager` is not a word anyone says. */
  readonly roleLabel = roleLabel;

  private readonly api = inject(ApiService);

  readonly roles = signal<RoleDto[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.api.roles().subscribe({
      next: (r) => { this.roles.set(r); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
}
