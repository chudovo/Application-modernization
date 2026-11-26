import { CurrencyPipe, DatePipe, I18nPluralPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { compareLikeOrderBy, matchesAnyField } from './angularjs-compat';
import { Order, OrdersApi } from './orders-api';

type Column = 'customer' | 'city' | 'total' | 'placed';

@Component({
  selector: 'app-order-list',
  imports: [FormsModule, RouterLink, CurrencyPipe, DatePipe, I18nPluralPipe],
  template: `
    <p>
      <input data-test="search" type="search" placeholder="Search" [ngModel]="search()" (ngModelChange)="search.set($event)" />
      <select data-test="status" [ngModel]="status()" (ngModelChange)="status.set($event); load()">
        <option value="">All</option>
        <option value="new">New</option>
        <option value="shipped">Shipped</option>
        <option value="cancelled">Cancelled</option>
      </select>
    </p>

    <p data-test="count">{{ visible().length | i18nPlural: countWords }}</p>

    <table>
      <thead>
        <tr>
          <th data-test="sort-customer" (click)="sortBy('customer')">Customer</th>
          <th data-test="sort-city" (click)="sortBy('city')">City</th>
          <th data-test="sort-total" (click)="sortBy('total')">Total</th>
          <th data-test="sort-placed" (click)="sortBy('placed')">Placed</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        @for (order of visible(); track order.id) {
          <tr data-test="row">
            <td data-test="customer"><a [routerLink]="['/orders', order.id]">{{ order.customer }}</a></td>
            <td data-test="city">{{ order.city }}</td>
            <td data-test="total">{{ order.total | currency }}</td>
            <td data-test="placed">{{ order.placed | date: 'mediumDate' : 'UTC' }}</td>
            <td data-test="status-cell">{{ order.status }}</td>
          </tr>
        }
      </tbody>
    </table>
  `,
})
export class OrderList {
  private readonly api = inject(OrdersApi);

  readonly orders = signal<Order[]>([]);
  readonly search = signal('');
  readonly status = signal('');
  readonly sort = signal<Column>('placed');
  readonly reverse = signal(true);
  readonly countWords = { '=0': 'No orders', '=1': '1 order', other: '# orders' };

  readonly visible = computed(() => {
    const column = this.sort();
    const direction = this.reverse() ? -1 : 1;
    return this.orders()
      .filter((order) => matchesAnyField(order, this.search()))
      .sort((a, b) => direction * compareLikeOrderBy(a[column], b[column]));
  });

  constructor() {
    this.load();
  }

  load() {
    this.api.list(this.status()).subscribe((orders) => this.orders.set(orders));
  }

  sortBy(column: Column) {
    this.reverse.set(this.sort() === column ? !this.reverse() : false);
    this.sort.set(column);
  }
}
