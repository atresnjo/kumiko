namespace DiffViewer.E2E;

/// <summary>Realistic TypeScript and TSX sources for highlighting checks and the 2 MB timing corpus.</summary>
static class LexerSamples
{
    public const string ReactComponent = """
        import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
        import type { Order, OrderStatus } from "./types";
        import * as api from '../api/client';

        interface OrderListProps {
          orders: readonly Order[];
          onSelect?: (order: Order, index: number) => void;
          filter: OrderStatus | "all";
        }

        const statusLabel: Record<OrderStatus, string> = {
          pending: "Pending",
          shipped: 'Shipped',
          cancelled: `Cancelled`,
        };

        /** Lists orders with a live total. */
        export function OrderList({ orders, onSelect, filter = "all" }: OrderListProps) {
          const [query, setQuery] = useState<string>("");
          const [page, setPage] = useState(0);
          const inputRef = useRef<HTMLInputElement | null>(null);
          const pageSize = 25;

          useEffect(() => {
            inputRef.current?.focus();
            const timer = window.setTimeout(() => setPage(0), 1_000);
            return () => window.clearTimeout(timer);
          }, [query]);

          const visible = useMemo(
            () =>
              orders
                .filter((o) => filter === "all" || o.status === filter)
                .filter((o) => o.customer.name.toLowerCase().includes(query.trim().toLowerCase()))
                .slice(page * pageSize, (page + 1) * pageSize),
            [orders, filter, query, page],
          );

          const total = visible.reduce((sum, o) => sum + o.amount / 100, 0);
          const handleClick = useCallback((order: Order, i: number) => onSelect?.(order, i), [onSelect]);

          if (!visible.length) {
            return <p className="empty">No orders match &quot;{query}&quot;.</p>;
          }

          return (
            <section className="orders" data-page={page} aria-label="Order list">
              <input
                ref={inputRef}
                value={query}
                placeholder="Search customers"
                onChange={(e) => setQuery(e.target.value)}
              />
              {/* Rows are keyed by id */}
              <ul>
                {visible.map((order, i) => (
                  <li key={order.id} className={order.status === "shipped" ? "done" : undefined}>
                    <button type="button" onClick={() => handleClick(order, i)}>
                      {order.customer.name} &mdash; {statusLabel[order.status]}
                    </button>
                    <span className="amount">{(order.amount / 100).toFixed(2)}</span>
                  </li>
                ))}
              </ul>
              <>
                <footer>Total: {total.toLocaleString(undefined, { style: "currency", currency: "USD" })}</footer>
              </>
            </section>
          );
        }

        export default OrderList;
        """;

    public const string DecoratedClass = """
        import { Component, Injectable, Input, Output, EventEmitter } from '@angular/core';
        import { HttpClient, HttpParams } from "@angular/common/http";

        export enum Level { Debug = 0, Info = 1, Warn = 0x2, Error = 0b11 }

        @Injectable({ providedIn: 'root' })
        export class TelemetryService {
          static readonly #instances = new Set<TelemetryService>();
          private readonly buffer: Array<{ at: number; text: string }> = [];
          protected level: Level = Level.Info;
          #flushes = 0n;

          constructor(private readonly http: HttpClient, @Inject(CONFIG) public config: Config) {
            TelemetryService.#instances.add(this);
          }

          get size(): number {
            return this.buffer.length;
          }

          set threshold(value: Level) {
            this.level = value ?? Level.Info;
          }

          log(text: string, level = Level.Info): void {
            if (level < this.level) return;
            const stamp = Date.now() / 1e3;
            this.buffer.push({ at: stamp, text: text.replace(/\s+/g, " ").trim() });
            if (this.buffer.length >= 512 || /^(fatal|panic):/i.test(text)) {
              void this.flush();
            }
          }

          async flush(): Promise<void> {
            const params = new HttpParams().set("n", String(this.buffer.length));
            try {
              await this.http.post<{ ok: boolean }>(`${this.config.endpoint}/v1/logs?batch=${this.#flushes++}`, this.buffer, { params }).toPromise();
              this.buffer.length = 0;
            } catch (error: unknown) {
              console.error(`flush failed: ${(error as Error).message}`);
            } finally {
              this.config.onFlush?.();
            }
          }
        }

        @Component({
          selector: 'app-badge',
          template: `<span class="badge">{{ label }}</span>`,
        })
        export class BadgeComponent {
          @Input() label = '';
          @Input({ required: true }) count!: number;
          @Output() readonly cleared = new EventEmitter<void>();

          ratio(total: number): number {
            return total === 0 ? 0 : this.count / total / 2;
          }
        }
        """;

    public const string TypeUtilities = """
        // Type-level helpers shared by the client and server.
        export type Primitive = string | number | boolean | bigint | symbol | null | undefined;

        export type DeepReadonly<T> = T extends Primitive
          ? T
          : T extends ReadonlyArray<infer U>
            ? ReadonlyArray<DeepReadonly<U>>
            : { readonly [K in keyof T]: DeepReadonly<T[K]> };

        export type Paths<T, Prefix extends string = ""> = {
          [K in keyof T & string]: T[K] extends object ? Paths<T[K], `${Prefix}${K}.`> : `${Prefix}${K}`;
        }[keyof T & string];

        export interface Result<TValue, TError = Error> {
          readonly ok: boolean;
          value?: TValue;
          error?: TError;
        }

        declare module "./events" {
          interface EventMap {
            "user:login": { id: string; at: Date };
          }
        }

        export const isDefined = <T,>(value: T | null | undefined): value is T => value !== null && value !== undefined;

        export function assertNever(value: never, message = `Unexpected value: ${JSON.stringify(value)}`): never {
          throw new Error(message);
        }

        export function groupBy<T, K extends PropertyKey>(items: Iterable<T>, key: (item: T) => K): Map<K, T[]> {
          const groups = new Map<K, T[]>();
          for (const item of items) {
            const k = key(item);
            const bucket = groups.get(k) ?? [];
            bucket.push(item);
            groups.set(k, bucket);
          }
          return groups;
        }

        export const clamp = (n: number, lo = -Infinity, hi = Infinity) => Math.min(hi, Math.max(lo, n));

        export function parseDuration(text: string): number | undefined {
          const match = /^(\d+(?:\.\d+)?)\s*(ms|s|m|h)$/.exec(text.trim());
          if (!match) return undefined;
          const scale = { ms: 1, s: 1e3, m: 60_000, h: 3.6e6 } satisfies Record<string, number>;
          return Number(match[1]) * scale[match[2] as keyof typeof scale];
        }

        export async function* paginate<T>(fetchPage: (cursor?: string) => Promise<{ items: T[]; next?: string }>) {
          let cursor: string | undefined;
          do {
            const { items, next } = await fetchPage(cursor);
            yield* items;
            cursor = next;
          } while (cursor);
        }

        export const enumValues = Object.freeze({ a: 0xff_ff, b: 0o755, c: 1.5e-3, d: .25, e: 10n ** 2n });
        const ratio = total / count / 2, re = /[/\]]+/g, half = (a + b) / 2;
        let mask = flags >>> 2 | (bits & ~0b1010) ^ 0x0f;
        mask >>>= 1;
        mask **= 2;
        settings.retries ??= 3;
        const deep = config?.server?.ports?.[0] ?? 8080;
        """;

    public static string[] Lines(string text) => text.Split('\n');
}
