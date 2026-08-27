import { Injectable, NgZone, effect, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from './auth.service';

export interface TaskChangedEvent {
  taskId: number;
  taskNumber: string;
  status: string;
  assigneeUserId?: number | null;
  kind: 'Created' | 'Updated';
}

export interface RequestChangedEvent {
  requestId: number;
  requestNumber: string;
  status: string;
  requesterUserId: number;
  kind: 'Created' | 'Updated';
}

export interface WorkforceChangedEvent {
  userId: number;
  state: string;
}

export interface VerificationChangedEvent {
  verificationId: number;
  verificationNumber: string;
  status: string;
  assignedToUserId?: number | null;
  kind: 'Created' | 'Updated';
}

export interface NotificationRaisedEvent {
  recipientUserId: number;
  notificationId: number;
  title: string;
}

/**
 * The real-time channel.
 *
 * The server sends thin payloads on purpose — an id, a number, a status — and the database is the
 * source of truth. So nothing here patches local state from an event. Screens subscribe, decide the
 * event is relevant to what they are showing, and **re-fetch**. That is what keeps a client correct
 * when two events arrive out of order, or when one is missed entirely during a reconnect.
 *
 * The hub is notification-only; it has no method that changes anything. Commands go over REST.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);
  private readonly zone = inject(NgZone);

  private connection: HubConnection | null = null;
  private readonly subscribedTasks = new Set<number>();
  private readonly subscribedVerifications = new Set<number>();

  readonly connected = signal(false);

  readonly taskChanged = new Subject<TaskChangedEvent>();
  readonly requestChanged = new Subject<RequestChangedEvent>();
  readonly workforceChanged = new Subject<WorkforceChangedEvent>();
  readonly verificationChanged = new Subject<VerificationChangedEvent>();
  readonly notificationRaised = new Subject<NotificationRaisedEvent>();

  constructor() {
    // Follow the session: connect when signed in, drop the socket when signed out.
    effect(() => {
      if (this.auth.isAuthenticated()) {
        void this.connect();
      } else {
        void this.disconnect();
      }
    });
  }

  private async connect(): Promise<void> {
    if (this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/workflow', {
        // A browser cannot set headers on a WebSocket handshake, so the token rides in the query
        // string. The API's JwtBearerEvents reads it for /hubs paths only.
        accessTokenFactory: () => this.auth.accessToken ?? '',
      })
      // Default backoff, then keep trying: an internal tool left open overnight should recover
      // on its own rather than needing a refresh.
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('taskChanged', (e: TaskChangedEvent) => this.emit(this.taskChanged, e));
    connection.on('requestChanged', (e: RequestChangedEvent) => this.emit(this.requestChanged, e));
    connection.on('workforceChanged', (e: WorkforceChangedEvent) => this.emit(this.workforceChanged, e));
    connection.on('verificationChanged', (e: VerificationChangedEvent) => this.emit(this.verificationChanged, e));
    connection.on('notification', (e: NotificationRaisedEvent) => this.emit(this.notificationRaised, e));

    connection.onreconnected(() => {
      this.zone.run(() => this.connected.set(true));
      // Group membership is per-connection. The server re-adds the user and permission groups in
      // OnConnectedAsync, but task subscriptions were ours to make, so we re-make them.
      for (const taskId of this.subscribedTasks) {
        void connection.invoke('SubscribeToTask', taskId).catch(() => undefined);
      }
      for (const verificationId of this.subscribedVerifications) {
        void connection.invoke('SubscribeToVerification', verificationId).catch(() => undefined);
      }
    });

    connection.onreconnecting(() => this.zone.run(() => this.connected.set(false)));
    connection.onclose(() => this.zone.run(() => this.connected.set(false)));

    this.connection = connection;

    try {
      await connection.start();
      this.zone.run(() => this.connected.set(true));
    } catch {
      // Real-time is a convenience, never a requirement. The app works without it; the user just
      // has to refresh to see other people's changes.
      this.zone.run(() => this.connected.set(false));
    }
  }

  private async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.subscribedTasks.clear();
    this.subscribedVerifications.clear();
    this.connected.set(false);

    if (connection) {
      await connection.stop().catch(() => undefined);
    }
  }

  /** Join the group for one task — call it when a task screen opens. */
  subscribeToTask(taskId: number): void {
    this.subscribedTasks.add(taskId);

    if (this.connection?.state === HubConnectionState.Connected) {
      void this.connection.invoke('SubscribeToTask', taskId).catch(() => undefined);
    }
  }

  unsubscribeFromTask(taskId: number): void {
    this.subscribedTasks.delete(taskId);

    if (this.connection?.state === HubConnectionState.Connected) {
      void this.connection.invoke('UnsubscribeFromTask', taskId).catch(() => undefined);
    }
  }

  /** Join the group for one verification — call it when a verification screen opens. */
  subscribeToVerification(verificationId: number): void {
    this.subscribedVerifications.add(verificationId);

    if (this.connection?.state === HubConnectionState.Connected) {
      void this.connection.invoke('SubscribeToVerification', verificationId).catch(() => undefined);
    }
  }

  unsubscribeFromVerification(verificationId: number): void {
    this.subscribedVerifications.delete(verificationId);

    if (this.connection?.state === HubConnectionState.Connected) {
      void this.connection.invoke('UnsubscribeFromVerification', verificationId).catch(() => undefined);
    }
  }

  /** SignalR callbacks arrive outside Angular's context; bring them back in. */
  private emit<T>(subject: Subject<T>, event: T): void {
    this.zone.run(() => subject.next(event));
  }
}
