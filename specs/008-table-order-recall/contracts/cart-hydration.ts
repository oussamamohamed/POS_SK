export interface ActiveOrderLine {
  lineId: string;
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  taxRatePercent: number;
  preparationStationId?: string;
  isDispatched: boolean;
  modifiersSummary: string[];
}

export interface ActiveTableOrder {
  orderId: string;
  tableNumber: string;
  waiterName?: string;
  coversCount: number;
  openedAtUtc: string;
  lines: ActiveOrderLine[];
  totalHtAmount: number;
  totalVatAmount: number;
  totalTtcAmount: number;
}

export interface TableOrderClientService {
  getActiveOrder(tableNumber: string): Promise<ActiveTableOrder | null>;
  saveTableItems(tableNumber: string, items: { productId: string; quantity: number }[]): Promise<ActiveTableOrder>;
  dispatchKitchenOrder(tableNumber: string): Promise<boolean>;
  clearTable(tableNumber: string): Promise<boolean>;
}
