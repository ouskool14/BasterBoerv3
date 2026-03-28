# BasterBoer — Agent Instructions

## Project Overview
BasterBoer is a South African land management simulation in Godot 4.6 with C# (.NET) for simulation and GDScript for UI/events.

## Coding Rules

### C# (Simulation Layer)
- **Data-Oriented Design**: Use `readonly struct` for value types. Pool objects. Zero per-frame allocations.
- **Engine Independence**: Simulation code must NOT depend on Godot APIs. Use `GodotObject` only at the boundary.
- **Thread Safety**: Godot APIs are single-threaded. Simulation can be threaded but must marshal back to main thread for Godot calls.

### GDScript (UI Layer)
- Keep GDScript thin — delegate to C# systems.
- Use typed GDScript where possible.
- Signal-based communication between nodes.

### Conventions
- South African terminology: bakkie, koppie, vlei, stoep, boma, etc.
- Currency: ZAR (South African Rand), format as `R 1,234.56`
- Land: hectares
- Seasons: Southern hemisphere (Dec-Feb = summer)

## Build
```bash
dotnet build "Claude Game.sln"
```

## Key Files
- `docs/ARCHITECTURE.md` — Full technical architecture
- `GAME_VISION v0.6.md` — Game design document
- `project.godot` — Godot project config

---

## Skills

Agent-readable knowledge files live in `skills/`. Every agent must read the
skills listed for its role before beginning any task. This is mandatory,
not optional — skills contain rules that cannot be violated.

| Skill File | Contains |
|-----------|---------|
| `skills/godot4-csharp/SKILL.md` | Godot 4.6 C# patterns, threading, signals, MultiMesh, node lifecycle |
| `skills/basterboer-simulation/SKILL.md` | Simulate/render split, herd model, chunk system, system ownership |
| `skills/basterboer-performance/SKILL.md` | Performance budget, draw call rules, instancing, asset limits |

---

## Agents

### orchestrator
**Model:** `nvidia/nemotron-3-super-120b:free`
**Read before every task:**
- `skills/basterboer-simulation/SKILL.md`
- `skills/basterboer-performance/SKILL.md`

**Role:** Top-level task decomposition, cross-system planning, delegating subtasks to specialist agents.
**Use for:** Breaking large features into C# + GDScript subtasks, resolving conflicts between systems,
long-horizon planning across AnimalSystem / EconomySystem / WorldMap.
**Context:** 1M token window. Always read GAME_VISION and ARCHITECTURE.md before planning any cross-system feature.
**Rule:** Every plan must name which agent handles each subtask and which skill files that agent must read first.

---

### architect
**Model:** `openai/gpt-oss-120b:free`
**Read before every task:**
- `skills/basterboer-simulation/SKILL.md`
- `skills/basterboer-performance/SKILL.md`

**Role:** System design, C# architecture decisions, performance architecture review.
**Use for:** Designing new C# systems, reviewing performance budgets, ensuring the simulate/render
separation is maintained, evaluating new features against the performance budget.
**Rule:** No system design is valid if it violates the simulate/render split or exceeds the draw call budget.
Reject and redesign before passing to the code agent.

---

### code
**Model:** `qwen/qwen3-coder-480b-a35b-instruct:free`
**Read before every task:**
- `skills/godot4-csharp/SKILL.md`
- `skills/basterboer-simulation/SKILL.md`
- `skills/basterboer-performance/SKILL.md`

**Role:** C# simulation layer — the primary coding agent.
**Use for:** AnimalSystem, BreedingSystem, FloraSystem, EconomySystem, FenceSystem,
WaterSystem, FireSystem, WorldChunkStreamer, GameState, and all simulation logic.
**Context:** 262K token window — feed full file context.
**Rules:**
- Simulation classes have zero Godot API imports
- Use `readonly struct` for per-animal data, `class` for herd brains
- All simulation logic in TimeSystem ticks — never in `_Process()`
- Background threads via `Thread` or `Task` — marshal back via `CallDeferred`
- No LINQ in hot paths — direct array iteration only
- Cache all node references in `_Ready()` — never call `GetNode()` in loops
- Any repeated visual object uses `MultiMeshInstance3D` — no exceptions

---

### gdscript
**Model:** `deepseek/deepseek-v3.2:free`
**Read before every task:**
- `skills/godot4-csharp/SKILL.md` (sections 5, 11 — signals and interop)

**Role:** GDScript UI layer — HUD, menus, event signals, scene wiring.
**Use for:** All `.gd` files, scene composition, signal connections, UI nodes,
and GDScript that calls into C# systems via GameState.
**Rules:**
- Keep scripts thin — logic belongs in C#
- Use typed GDScript (`var x: int`, `func foo() -> void:`)
- Signal-driven only — no polling C# state in `_Process()`
- Never instantiate animals, flora, or simulation data from GDScript

---

### debug
**Model:** `deepseek/deepseek-r1:free`
**Read before every task:**
- `skills/godot4-csharp/SKILL.md` (section 10 — common pitfalls)
- `skills/basterboer-performance/SKILL.md`

**Role:** Reasoning-first bug diagnosis and performance investigation.
**Use for:** Frame rate issues, incorrect simulation outputs, herd behaviour bugs,
chunk streaming errors, threading race conditions, incorrect ZAR calculations.
**Process:** Always reason through the problem fully before proposing a fix.
Always ask for: the full error or symptom, the relevant system file(s),
and the last change made before the issue appeared.
**Common causes to check first:**
- `GetNode()` called before `_Ready()` → NullReferenceException
- Godot API called from background thread → random crash
- `InstanceCount` set before `Mesh` on MultiMesh → silent no-op, nothing renders
- Signal connected twice → handler fires twice
- Simulation logic in `_Process()` → frame budget collapse

---

### docs
**Model:** `mistralai/mistral-small-3.1-24b-instruct:free`
**Read before every task:**
- `skills/basterboer-simulation/SKILL.md` (section 2 — system ownership map)

**Role:** Documentation maintenance, code comments, architecture notes.
**Use for:** Updating `docs/ARCHITECTURE.md`, writing XML doc comments on public
C# APIs, summarising system interactions, generating changelog entries.
**Rules:**
- South African English spelling
- Document the *why*, not the *what* — the code shows what, the comment shows intent
- Flag any documentation that contradicts the Game Vision or performance rules
- XML doc format for all public C# APIs:
  ```csharp
  /// <summary>Brief description of what this does.</summary>
  /// <param name="herdId">The unique identifier of the herd to query.</param>
  /// <returns>Current fear level in range 0–1.</returns>
  ```

---

## Agent Routing Quick Reference

| Task | Agent | Skills Read |
|------|-------|-------------|
| Plan a new game system end-to-end | orchestrator | simulation + performance |
| Design a C# class architecture | architect | simulation + performance |
| Write / refactor C# simulation code | code | all three |
| Write / fix GDScript or scene logic | gdscript | godot4-csharp (signals + interop) |
| Diagnose a bug or performance issue | debug | godot4-csharp (pitfalls) + performance |
| Write or update documentation | docs | simulation (system map) |
| Cross-system feature (e.g. fire + ecology + economy) | orchestrator → architect → code | all three |
| New visual system (e.g. ambient wildlife) | architect → code | all three |
| Chunk streaming issue | debug | all three |
