# Kumiko Todo for iOS

Two instances of the reusable `TodoListView` from `examples/Todo.Components` in a
UIKit navigation stack. The overview lists Work and Home; selecting one pushes
its detail page, where tasks can be added, ticked, renamed, filtered and cleared.

The model owns everything: the lists, the page path
(`NavigationPath<TodoPage, TodoEntry>`) rendered by `Ui.Stack` through the
generated `TodoPageRoutes`, and the root screen's one-visit
`NavigationHistory<string>`. Back is a versioned request that the update
checks. Leaving a detail page with a new-task draft or a rename in progress asks
first: Keep editing retains the draft, editing withdraws the confirmation, and
Discard and leave clears that list's drafts. Stale or foreign requests do
nothing, and UIKit's swipe does not begin on a guarded page.

```sh
dotnet build examples/Todo.iOS -c Release -r iossimulator-arm64 -p:KumikoCompileMetal=false
```

Data is in memory; nothing is saved between launches.
