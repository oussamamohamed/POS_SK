export interface CartProduct {
  id: string;
  name: string;
  price: number;
  taxRatePercent: number;
  preparationStationId: string;
}

export interface CartLine {
  lineId?: string;
  product: CartProduct;
  quantity: number;
  isDispatched: boolean;
  modifiers: string[];
}

export interface TableCartState {
  activeTable: string;
  activeOrderId: string | null;
  activeCovers: number;
  cart: CartLine[];
}

export interface ClearCartResult {
  clearedUndispatchedCount: number;
  preservedDispatchedCount: number;
  message: string;
  messageType: 'info' | 'error' | 'success';
}
