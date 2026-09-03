export interface UpdateProductPayload {
  name: string;
  categoryId: string;
  price: number;
  taxRatePercent: number;
  description?: string;
  colorHex?: string;
  displayOrder?: number;
  isQuickKey: boolean;
  stationId?: string;
}

export interface UpdateCategoryPayload {
  name: string;
  colorHex?: string;
  displayOrder?: number;
  iconName?: string;
}

export interface UpdateStaffPayload {
  name: string;
  role: string;
  pin?: string;
  isActive: boolean;
}

export interface UpdatePrinterPayload {
  name: string;
  ipAddress: string;
  port: number;
  paperWidthMm: number;
  hasCashDrawer: boolean;
  targetStations: string[];
}
