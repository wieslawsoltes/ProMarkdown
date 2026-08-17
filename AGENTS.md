# Engineering Standards for Agents

This document is mandatory for any human or AI agent contributing to this repository.

These rules are not optional. If a requested implementation conflicts with them, the work must be redesigned to satisfy them rather than bypassing them.

## Mission

Build production-grade C#/.NET software with:

- strict architectural discipline
- professional MVVM composition
- measurable performance awareness
- Fluent-first UI quality
- maintainable, testable, reviewable code

## Non-Negotiable Core Principles

The following principles are mandatory on every change:

- **SOLID**
  - Single Responsibility Principle
  - Open/Closed Principle
  - Liskov Substitution Principle
  - Interface Segregation Principle
  - Dependency Inversion Principle
- **DRY**: do not duplicate logic, parsing rules, UI state rules, or protocol handling.
- **KISS**: prefer the simplest correct design that remains extensible.
- **YAGNI**: do not add speculative abstractions or features without a real need.
- **Separation of Concerns**: keep UI, presentation, domain logic, transport, persistence, and infrastructure clearly separated.
- **Composition over Inheritance**: inherit only when the type relationship is real and beneficial.
- **Fail Fast**: invalid state, broken assumptions, and protocol errors must surface clearly.
- **Explicitness over Magic**: prefer readable contracts and explicit flow over hidden coupling.
- **Correctness before cleverness**: no shortcut is acceptable if it harms reliability or maintainability.

## Architecture Rules for C#/.NET Applications

- Keep classes focused and small enough to have one clear reason to change.
- Depend on abstractions at boundaries. UI must not directly own transport or infrastructure details.
- Prefer constructor injection over service location.
- Model workflows as clear services, state models, and view models with explicit responsibilities.
- Avoid god objects, static mutable state, and hidden global coupling.
- Use immutable data and readonly members where practical.
- Prefer strong typing over stringly typed protocols, flags, or dictionaries.
- Keep public APIs minimal, intentional, and documented by naming and structure.
- Make invalid states difficult to represent.

## Mandatory MVVM Rules

- MVVM is required. Do not place business logic in views.
- Views are for composition, bindings, styling, animation, and view-only interaction concerns.
- ViewModels own presentation state, commands, workflow orchestration, and UI-facing projections.
- Models/services own data contracts, domain behavior, protocol calls, and infrastructure integration.
- Code-behind is allowed only for view-specific concerns such as focus, scrolling, pointer routing, platform view services, and visual-state mechanics that do not belong in the ViewModel.
- Do not access transport, persistence, or environment state directly from XAML views.
- Commands must be explicit, testable, and state-aware.
- Property change flow must be deterministic; avoid hidden side effects.
- Bindings should be strongly typed where available and should avoid reflection-heavy patterns when better options exist.

## C# Coding Standards

- Use nullable reference types correctly. Do not silence warnings without justification.
- Prefer async/await end-to-end for asynchronous workflows.
- Flow `CancellationToken` through cancellable operations.
- Dispose and asynchronously dispose resources correctly.
- Prefer interfaces for boundaries, but do not create empty abstractions with no value.
- Prefer pattern matching, switch expressions, and modern C# features when they improve clarity.
- Avoid broad catch blocks. Catch only what you can handle meaningfully.
- Do not swallow exceptions.
- Prefer clear naming over comments. Add comments only when intent is not obvious from the code itself.
- Do not use `dynamic`, weak typing, or unsafe casts unless there is no better option and the reason is documented in code review.
- Avoid temporal coupling and hidden ordering requirements between method calls.

## Mandatory Performance Standards

Performance is not negotiable. Every agent must actively avoid unnecessary allocations, needless copies, blocking, and UI-thread abuse.

For hot paths, protocol code, parsing, formatting, rendering support, data transforms, and repeated operations, contributors must evaluate and prefer high-performance .NET primitives and patterns, including where appropriate:

- `Span<T>` / `ReadOnlySpan<T>`
- `Memory<T>` / `ReadOnlyMemory<T>`
- `stackalloc`
- `ArrayPool<T>` / `MemoryPool<T>`
- `StringBuilder` or allocation-aware string construction
- `ValueTask` when it is materially beneficial and semantically correct
- `readonly struct`, `record struct`, `in`, `ref`, and `ref readonly` where they reduce copying safely
- `CollectionsMarshal` and similar low-overhead APIs when justified and safe
- SIMD via `Vector<T>` and hardware-accelerated numerics where loops are vectorizable and measurable
- source-generated or low-allocation serialization patterns where applicable

### Performance Rules

- Do not allocate in tight loops unless unavoidable.
- Do not use LINQ on hot paths when it causes avoidable allocations or repeated enumeration.
- Do not convert between strings, arrays, and collections gratuitously.
- Avoid boxing, delegate churn, closure allocations, and repeated temporary object creation in frequently executed code.
- Keep UI-thread work minimal and deterministic.
- Batch UI updates when possible.
- Avoid sync-over-async and blocking waits.
- Measure before and after meaningful performance work. Optimization must be evidence-driven, not guessed.
- Use the highest-performance primitive that still preserves readability, safety, and correctness.

## Collection, Memory, and API Design Rules

- Expose the narrowest useful abstraction: `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, or spans where appropriate.
- Avoid returning mutable collections when callers should not mutate them.
- Avoid copying large collections or buffers unless required for ownership or safety.
- Prefer streaming and incremental processing over materializing large intermediate objects.
- Design APIs to make ownership and lifetime clear.

## Threading and Responsiveness Rules

- UI must remain responsive at all times.
- Any expensive work must stay off the UI thread.
- Marshal to the UI thread only for UI state mutation that truly requires it.
- Avoid race-prone state transitions; use clear ownership and synchronization.
- Cancellation and teardown paths must be deliberate and reliable.

## Mandatory Fluent Design and Professional UI Standards

The application must follow best Fluent design practices and must look professional. This is not optional.

### Visual System

- Use a coherent Fluent-first design language across layout, motion, controls, colors, typography, elevation, and spacing.
- Use design tokens and shared resources for colors, spacing, radii, typography, icon sizes, and surfaces.
- Do not scatter ad-hoc brushes, magic numbers, or one-off styles through the UI.
- Favor calm, modern, high-contrast surfaces with disciplined accent use.
- Respect density and hierarchy: primary actions must be obvious, secondary actions must be quieter, destructive actions must be unmistakable.

### Layout and Composition

- Layout must be balanced, aligned, and consistent across the entire shell.
- Use predictable spacing scales and consistent control sizing.
- Avoid clutter. Every surface must have a clear purpose.
- Empty, loading, success, warning, and error states must be designed intentionally.
- Responsive behavior must preserve primary workflows at smaller sizes without visual collapse.

### Typography and Content

- Typography must clearly communicate hierarchy and scanning order.
- Use concise labels, high-signal helper text, and consistent command wording.
- Avoid dense walls of text in primary workflows.
- Text contrast and readability are mandatory.

### Interaction and Accessibility

- Keyboard navigation, focus order, and visible focus treatment are required.
- Pointer, touch, and keyboard interactions must feel deliberate and consistent.
- Use accessible color contrast and avoid relying on color alone to communicate state.
- Automation and accessibility semantics should be present for important controls and workflows.

### Fluent Quality Bar

Every UI change should feel:

- polished
- intentional
- modern
- calm
- trustworthy
- consistent with Fluent interaction and visual principles

If a UI is merely functional but not visually professional, it is not complete.

## XAML and Avalonia Rules

- Prefer reusable styles, resources, and control patterns over duplicated XAML fragments.
- Prefer typed/compiled bindings and explicit data context design where feasible.
- Keep XAML readable; complex visual logic belongs in reusable controls or view support structures.
- Use code-behind only for legitimate view concerns, not application logic.
- Keep themes, tokens, and visual states centralized.

## Testing and Validation Requirements

No change is complete without validation appropriate to its scope.

- Build must pass.
- Existing tests must pass.
- New logic with meaningful behavior changes should gain tests where practical.
- UI changes must be visually and behaviorally checked for regressions.
- Performance-sensitive changes should be validated for allocation and responsiveness impact when relevant.

## Code Review Gates

Before considering work complete, every agent must verify:

- SOLID responsibilities are respected.
- MVVM boundaries are intact.
- naming is clear and intention-revealing.
- performance pitfalls were actively considered.
- no unnecessary allocations or UI-thread abuse were introduced.
- Fluent design quality and professional polish are present.
- accessibility and interaction consistency are preserved.
- duplication was not introduced.

## Prohibited Shortcuts

The following are not acceptable:

- business logic in views
- large monolithic view models with unrelated responsibilities
- copy-pasted logic instead of extraction or reuse
- hidden mutable global state
- broad exception swallowing
- speculative abstractions with no real use case
- ad-hoc styling that bypasses the design system
- premature micro-optimization without understanding the path
- ignoring hot-path allocation issues when the code is clearly performance-sensitive
- shipping UI that is technically functional but visually unpolished

## Definition of Done

Work is done only when all of the following are true:

- the implementation is correct
- the design follows SOLID and related architectural principles
- MVVM boundaries remain clean
- performance implications were considered and handled responsibly
- the UI is Fluent-aligned and professionally polished
- validation has been completed
- the code is maintainable for future contributors

When in doubt, choose the more disciplined, more maintainable, more performant, and more professional solution.
