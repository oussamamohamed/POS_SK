export enum CourseType {
  Direct = 'Direct',
  Suite = 'Suite',
  Dessert = 'Dessert',
  OnDemand = 'OnDemand'
}

export enum DiscountType {
  Percentage = 'Percentage',
  FixedAmount = 'FixedAmount',
  Comp = 'Comp'
}

export enum PaymentMethod {
  Cash = 'Cash',
  CreditCard = 'CreditCard',
  MealVoucher = 'MealVoucher',
  GiftCard = 'GiftCard',
  RoomCharge = 'RoomCharge'
}

export interface HotelRoomInfo {
  roomNumber: string;
  guestName: string;
  isOccupied: boolean;
  currentBalance: number;
  maxCreditLimit: number;
}

export interface TableTransferPayload {
  targetTableNumber: string;
  operatorId: string;
}

export interface ApplyDiscountPayload {
  discountType: DiscountType;
  value: number;
  reason: string;
  operatorId: string;
}

export interface RoomChargePayload {
  orderId: string;
  tableNumber: string;
  roomNumber: string;
  guestName: string;
  amount: number;
  tipAmount: number;
  signatureDataUrl?: string;
}
