export type TableStatus = 'Free' | 'Occupied' | 'BillRequested' | 'Paid';

export interface CreateTablePayload {
  tableNumber: string;
  capacity?: number;
  positionX?: number;
  positionY?: number;
}

export interface DiningTableDto {
  tableNumber: string;
  capacity: number;
  status: TableStatus | number;
  positionX: number;
  positionY: number;
  assignedWaiterName?: string | null;
  coversCount: number;
  activeOrderId?: string | null;
  openedAtUtc?: string | null;
}
