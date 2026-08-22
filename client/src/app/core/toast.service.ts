import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void {
    this.snackBar.open(message, 'Dismiss', {
      duration: 4000,
      panelClass: 'toast-success',
      horizontalPosition: 'right',
    });
  }

  /**
   * Errors stay up longer and always carry a dismiss action. A failure the user did not read is a
   * failure they will report as "nothing happened".
   */
  error(message: string): void {
    this.snackBar.open(message, 'Dismiss', {
      duration: 9000,
      panelClass: 'toast-error',
      horizontalPosition: 'right',
    });
  }

  info(message: string): void {
    this.snackBar.open(message, 'Dismiss', {
      duration: 5000,
      horizontalPosition: 'right',
    });
  }
}
