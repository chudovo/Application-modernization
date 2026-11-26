import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface Order {
  id: number;
  customer: string;
  city: string | null;
  total: number | null;
  placed: string;
  status: string;
  discount: number;
  note: string;
}

@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);

  list(status: string) {
    // AngularJS left parameters without a value out of the address. HttpClient sends "status=", and a backend
    // may well read that as "orders whose status is empty".
    return this.http.get<Order[]>('api/orders', { params: status ? { status } : {} });
  }

  get(id: string) {
    return this.http.get<Order>(`api/orders/${id}`);
  }

  save(id: number, change: Pick<Order, 'status' | 'discount'>) {
    return this.http.put<Order>(`api/orders/${id}`, change);
  }
}
