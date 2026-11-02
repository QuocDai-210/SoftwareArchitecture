"use client";
import { useState, type ReactNode } from "react";
import { RideService } from "@/features/rides/application/RideService";
import { HttpRideGateway } from "@/features/rides/infrastructure/HttpRideGateway";
import { RideDashboard } from "@/features/rides/presentation/RideDashboard";
import type { RideUser } from "@/features/rides/presentation/useRides";
export default function RideClient({ user, logout, mapKey, paymentReference }: { user: RideUser; logout: ReactNode; mapKey: string; paymentReference?: string }) {
  const [service] = useState(() => new RideService(new HttpRideGateway()));
  return <RideDashboard user={user} logout={logout} service={service} mapKey={mapKey} paymentReference={paymentReference} />;
}
