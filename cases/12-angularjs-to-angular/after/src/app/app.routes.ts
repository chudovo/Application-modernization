import { Routes } from '@angular/router';
import { OrderDetail } from './order-detail';
import { OrderList } from './order-list';

export const routes: Routes = [
  { path: 'orders', component: OrderList },
  { path: 'orders/:id', component: OrderDetail },
  { path: '**', redirectTo: 'orders' },
];
