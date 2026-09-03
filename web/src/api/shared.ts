/**
 * Matches WorkOrderPriority / RequestPriority in
 * src/Modules/WorkManagement/Domain/WorkOrderStatus.cs and
 * src/Modules/MaintenanceRequests/Domain/MaintenanceRequestStatus.cs — the same P1-P4 scale on
 * both, serialized as these literal strings by the shared JsonStringEnumConverter (Program.cs).
 */
export type Priority = 'P1' | 'P2' | 'P3' | 'P4'

export const priorityLabels: Record<Priority, string> = {
  P1: 'P1 — Emergency',
  P2: 'P2 — High',
  P3: 'P3 — Medium',
  P4: 'P4 — Low',
}
