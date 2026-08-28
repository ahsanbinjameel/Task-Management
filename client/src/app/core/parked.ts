/**
 * Capabilities built, kept, and deliberately not offered (PRODUCT-CORE §10).
 *
 * Hide is not delete. Every one of these still has its entity, its service, its endpoint, its
 * permission and its tests; what it does not have is a way in from the normal journey. The product
 * is under a freeze until the pilot runs (PRODUCT-CORE §0), and these are the concepts that cost a
 * reader attention without yet having earned it — a scenario being imaginable is not the bar.
 *
 * Flip one back to `false` when a real, observed, preferably repeated operational problem asks for
 * it. That is the whole decision, and it is one line.
 */
export interface ParkedCapabilities {
  /** The stopwatch for work that never came through the front door. */
  readonly quickWork: boolean;
  /** The task-to-task graph, and the blocked signal that comes off it. */
  readonly dependencies: boolean;
  /** Real tasks one level down, with their own number, assignee and timer. */
  readonly subtasks: boolean;
  /**
   * The record-then-approve ceremony around a change of scope. The *discipline* is not parked —
   * PRODUCT-CORE §6 keeps it, as a linked follow-up request in a later round.
   */
  readonly scopeChanges: boolean;
}

export const PARKED: ParkedCapabilities = {
  quickWork: true,
  dependencies: true,
  subtasks: true,
  scopeChanges: true,
};
