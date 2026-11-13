using DiffViewer.Core;

namespace DiffViewer.Sources;

/// <summary>Built-in sample changes for --demo: deterministic, so offscreen scenes and screenshots repeat exactly.</summary>
public static class Demo
{
    public static (string, string, string, List<FilePair>) Pairs() => ("kumiko-diff", "Demo changes", "",
    [
        Pair("src/api/client.ts", FileStatus.Modified, ClientBefore, ClientAfter),
        Pair("src/components/TodoList.tsx", FileStatus.Modified, TodoBefore, TodoAfter),
        Pair("src/legacy/polyfills.js", FileStatus.Deleted, "if (!Array.prototype.at) {\n  Array.prototype.at = function (i) { return this[i < 0 ? this.length + i : i]; };\n}\n", ""),
        Pair("src/utils/format.ts", FileStatus.Added, "", FormatAfter),
    ]);

    static FilePair Pair(string path, FileStatus status, string before, string after) =>
        new(path, null, status, TextFile.Split(before), TextFile.Split(after));

    const string ClientBefore = """
        import { Todo } from "../types";

        const BASE_URL = "https://api.example.com/v1";

        export async function fetchTodos(userId: string): Promise<Todo[]> {
          const response = await fetch(`${BASE_URL}/users/${userId}/todos`);
          if (!response.ok) {
            throw new Error("Request failed");
          }
          return response.json();
        }

        export async function saveTodo(todo: Todo): Promise<void> {
          await fetch(`${BASE_URL}/todos/${todo.id}`, {
            method: "PUT",
            body: JSON.stringify(todo),
          });
        }

        """;

    const string ClientAfter = """
        import { Todo } from "../types";
        import { retry } from "../utils/retry";

        const BASE_URL = process.env.API_URL ?? "https://api.example.com/v2";
        const TIMEOUT_MS = 8_000;

        export async function fetchTodos(userId: string, signal?: AbortSignal): Promise<Todo[]> {
          const response = await retry(() => fetch(`${BASE_URL}/users/${userId}/todos`, { signal }), 3);
          if (!response.ok) {
            throw new Error(`Request failed with ${response.status}`);
          }
          return (await response.json()) as Todo[];
        }

        export async function saveTodo(todo: Todo): Promise<void> {
          const controller = new AbortController();
          const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
          try {
            await fetch(`${BASE_URL}/todos/${todo.id}`, {
              method: "PATCH",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify(todo),
              signal: controller.signal,
            });
          } finally {
            clearTimeout(timer);
          }
        }

        """;

    const string TodoBefore = """
        import { useEffect, useState } from "react";
        import { fetchTodos } from "../api/client";
        import type { Todo } from "../types";

        export function TodoList({ userId }: { userId: string }) {
          const [todos, setTodos] = useState<Todo[]>([]);

          useEffect(() => {
            fetchTodos(userId).then(setTodos);
          }, [userId]);

          return (
            <ul className="todo-list">
              {todos.map((todo) => (
                <li key={todo.id}>{todo.title}</li>
              ))}
            </ul>
          );
        }

        """;

    const string TodoAfter = """
        import { useEffect, useMemo, useState } from "react";
        import { fetchTodos } from "../api/client";
        import { formatDue } from "../utils/format";
        import type { Todo } from "../types";

        interface Props {
          userId: string;
          showDone?: boolean;
        }

        export function TodoList({ userId, showDone = false }: Props) {
          const [todos, setTodos] = useState<Todo[]>([]);
          const visible = useMemo(() => todos.filter((t) => showDone || !t.done), [todos, showDone]);

          useEffect(() => {
            const controller = new AbortController();
            fetchTodos(userId, controller.signal).then(setTodos).catch(console.error);
            return () => controller.abort();
          }, [userId]);

          return (
            <ul className="todo-list" aria-label="Todos">
              {visible.map((todo) => (
                <li key={todo.id} data-done={todo.done}>
                  {todo.title} <time>{formatDue(todo.due)}</time>
                </li>
              ))}
            </ul>
          );
        }

        """;

    const string FormatAfter = """
        const relative = new Intl.RelativeTimeFormat("en", { numeric: "auto" });

        /** Formats a due date relative to now, such as "tomorrow" or "in 3 days". */
        export function formatDue(due: Date | undefined, now = new Date()): string {
          if (!due) return "";
          const days = Math.round((due.getTime() - now.getTime()) / 86_400_000);
          return Math.abs(days) < 7 ? relative.format(days, "day") : due.toLocaleDateString();
        }

        export const isOverdue = (due?: Date): boolean => !!due && due.getTime() < Date.now();

        """;
}
