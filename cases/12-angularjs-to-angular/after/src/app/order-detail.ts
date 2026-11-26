import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MEDIUM_AS_ANGULARJS } from './angularjs-compat';
import { Order, OrdersApi } from './orders-api';

@Component({
  selector: 'app-order-detail',
  imports: [FormsModule, RouterLink, CurrencyPipe, DatePipe],
  template: `
    <p><a data-test="back" routerLink="/orders">Back to the list</a></p>

    @if (notFound()) {
      <p data-test="not-found" class="error">Order not found</p>
    }

    @if (order(); as order) {
      <form #form="ngForm" novalidate (ngSubmit)="save(order)">
        <h2 data-test="title">Order {{ order.id }}: {{ order.customer }}</h2>
        <p data-test="summary">{{ order.total | currency }}, placed {{ order.placed | date: mediumFormat : 'UTC' }}</p>
        <p data-test="note" [innerHTML]="order.note"></p>

        <p>
          <label
            >Status
            <select data-test="status" name="status" [(ngModel)]="order.status">
              <option value="new">New</option>
              <option value="shipped">Shipped</option>
              <option value="cancelled">Cancelled</option>
            </select>
          </label>
        </p>
        <p>
          <label
            >Discount in percent
            <input data-test="discount" name="discount" type="number" [(ngModel)]="order.discount" #discount="ngModel" required min="0" max="50" />
          </label>
          @if (discount.errors?.['required']) {
            <span data-test="discount-error" class="error">Discount is required</span>
          }
          @if (discount.errors?.['min'] || discount.errors?.['max']) {
            <span data-test="discount-error" class="error">Discount must be between 0 and 50</span>
          }
        </p>
        <p>
          <button data-test="save" type="submit" [disabled]="form.invalid">Save</button>
          @if (saved()) {
            <span data-test="saved">Saved</span>
          }
        </p>
      </form>
    }
  `,
})
export class OrderDetail {
  private readonly api = inject(OrdersApi);
  readonly mediumFormat = MEDIUM_AS_ANGULARJS;

  /** The route parameter, bound by the router. */
  readonly id = input.required<string>();
  readonly order = signal<Order | null>(null);
  readonly notFound = signal(false);
  readonly saved = signal(false);

  constructor() {
    effect(() => {
      this.api.get(this.id()).subscribe({
        next: (order) => this.order.set(order),
        error: () => this.notFound.set(true),
      });
    });
  }

  save(order: Order) {
    this.saved.set(false);
    this.api.save(order.id, { status: order.status, discount: order.discount }).subscribe(() => this.saved.set(true));
  }
}
