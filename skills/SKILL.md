# SKILL: Godot 4.6 C# Development

## When to use this skill
Read this file before writing ANY C# code that touches Godot APIs, scene nodes,
signals, threading, or rendering. Also read before reviewing or debugging C# files
in this project.

---

## 1. The Golden Rule — Engine Boundary

Simulation code must NEVER import or call Godot APIs directly.
The boundary between simulation and engine is `GameState` and the render layer only.

```csharp
// WRONG — simulation system importing Godot
using Godot;
public class AnimalSystem {
    public void Tick() {
        var node = Engine.GetMainLoop(); // NEVER do this in a simulation class
    }
}

// CORRECT — simulation system is pure C#
public class AnimalSystem {
    public void Tick(float deltaTime) {
        // Pure C# logic only. No Godot imports.
    }
}
```

---

## 2. Node Lifecycle — Order Matters

Godot 4.6 node lifecycle in C#:

| Method | When Called | Use For |
|--------|-------------|---------|
| `_Ready()` | Node enters scene tree, all children ready | Cache node refs, connect signals, init |
| `_Process(double delta)` | Every frame (main thread) | Visual updates ONLY |
| `_PhysicsProcess(double delta)` | Fixed physics step | Physics queries only |
| `_Input(InputEvent e)` | Input events | Player input only |
| `_Notification(int what)` | Engine notifications | Cleanup on `NOTIFICATION_PREDELETE` |

**Critical:** `_Ready()` fires bottom-up (children before parents). Never assume a
sibling node is ready when your own `_Ready()` fires. Use `CallDeferred` to safely
call methods that depend on sibling readiness.

```csharp
// WRONG — sibling may not be ready
public override void _Ready() {
    GameState.Instance.Register(this); // GameState._Ready() may not have fired
}

// CORRECT — deferred call runs after all _Ready() calls complete
public override void _Ready() {
    CallDeferred(MethodName.Initialize);
}
private void Initialize() {
    // Now ALL nodes in the scene have had _Ready() called
    GameState.Instance.Register(this);
}
```

---

## 3. Threading Rules — Non-Negotiable

Godot's scene tree and all Node APIs are **main-thread only**. Any violation causes
random crashes, not clean errors.

### What is safe on background threads:
- Pure C# data manipulation (structs, classes, collections)
- Math calculations
- File I/O (not Godot ResourceLoader)
- Simulation ticks (AnimalSystem, FloraSystem, EconomySystem)
- Chunk data generation

### What is NEVER safe on background threads:
- ANY `Node` method call
- `AddChild()`, `RemoveChild()`, `QueueFree()`
- `GetNode()`, `FindChild()`
- Instantiating `PackedScene`
- Setting node properties (`.Visible`, `.Position`, `.Name`)
- Godot signals (`EmitSignal`)
- `GD.Print()` — use `Console.WriteLine()` on background threads

### Marshalling back to main thread:

```csharp
// Pattern 1: CallDeferred (simplest, for void methods)
private void BackgroundThread() {
    // ... heavy work ...
    CallDeferred(MethodName.BuildVisuals); // runs on next main thread frame
}

// Pattern 2: Callable.From (for lambdas)
private void BackgroundThread() {
    var results = DoHeavyWork();
    Callable.From(() => ApplyResults(results)).CallDeferred();
}

// Pattern 3: Thread + Join (as used in FenceSystem.cs)
_generationThread = new Thread(GenerateFenceDataThreaded);
_generationThread.IsBackground = true;
_generationThread.Start();
// In BuildVisuals():
_generationThread.Join(); // wait for thread before touching results
```

---

## 4. Node References — Cache Everything

```csharp
// WRONG — GetNode() in a loop or _Process() destroys performance
public override void _Process(double delta) {
    GetNode<Label>("HUD/StatusLabel").Text = GetStatus(); // never do this
}

// CORRECT — cache in _Ready()
private Label _statusLabel;
public override void _Ready() {
    _statusLabel = GetNode<Label>("HUD/StatusLabel");
}
public override void _Process(double delta) {
    _statusLabel.Text = GetStatus();
}
```

---

## 5. Signals in C#

### Declaring signals:
```csharp
[Signal] public delegate void HerdStateChangedEventHandler(int herdId, string newState);
```

### Connecting signals (C# to C#):
```csharp
// Preferred: typed connection
someNode.HerdStateChanged += OnHerdStateChanged;

// For GDScript compatibility use StringName:
someNode.Connect(SignalName.HerdStateChanged, Callable.From<int, string>(OnHerdStateChanged));
```

### Emitting:
```csharp
EmitSignal(SignalName.HerdStateChanged, herdId, newState);
```

### Disconnecting on cleanup:
```csharp
public override void _Notification(int what) {
    if (what == NotificationPredelete) {
        someNode.HerdStateChanged -= OnHerdStateChanged;
    }
}
```

---

## 6. MultiMeshInstance3D — The Right Pattern

Every repeated object in the world uses MultiMesh. This is how:

```csharp
// Setup
var multiMesh = new MultiMesh {
    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
    Mesh = myMesh,
    InstanceCount = positions.Count  // set AFTER Mesh, before setting transforms
};

// Set individual transforms
for (int i = 0; i < positions.Count; i++) {
    var basis = Basis.Identity;
    // Apply rotation if needed:
    basis = basis.Rotated(Vector3.Up, yaw);
    multiMesh.SetInstanceTransform(i, new Transform3D(basis, positions[i]));
}

// Per-instance colour (if shader supports it):
multiMesh.UseColors = true;
multiMesh.SetInstanceColor(i, new Color(r, g, b));

// Wrap in node and add to scene:
var mmi = new MultiMeshInstance3D { Multimesh = multiMesh };
AddChild(mmi);
```

**Critical:** Set `InstanceCount` AFTER assigning `Mesh`. Setting it before
results in a silent no-op and no instances render.

**Updating positions at runtime (e.g. animal movement):**
```csharp
// Only update the instances that moved — not the whole buffer
multiMesh.SetInstanceTransform(animalIndex, newTransform);
```

---

## 7. PackedScene Instantiation

```csharp
// Load once, instantiate many
[Export] public PackedScene AnimalScene;

// In _Ready() or lazy-load:
private Node3D SpawnAnimal(Vector3 position) {
    var instance = AnimalScene.Instantiate<Node3D>();
    instance.Position = position;
    AddChild(instance); // must be on main thread
    return instance;
}

// Extracting a mesh from a GLB PackedScene (see FenceSystem pattern):
Node root = packedScene.Instantiate<Node>();
MeshInstance3D mi = FindFirstMeshInstance(root); // depth-first search
Mesh mesh = mi.Mesh;
root.Free(); // free the temporary instance — keep only the mesh resource
```

---

## 8. Singleton Pattern (Godot Autoload style in C#)

```csharp
public partial class GameState : Node {
    private static GameState _instance;
    public static GameState Instance => _instance;

    public override void _Ready() {
        if (_instance != null && _instance != this) {
            QueueFree();
            return;
        }
        _instance = this;
    }
}
```

Access from anywhere on the main thread:
```csharp
var mapSize = GameState.Instance.MapSizeX;
```

Never access `GameState.Instance` from a background thread without locking or
reading the value before the thread starts.

---

## 9. Resource Loading

```csharp
// Synchronous (main thread only, causes frame hitch on large assets):
var mesh = GD.Load<Mesh>("res://assets/animals/impala.glb");

// Async (preferred for large assets):
ResourceLoader.LoadThreadedRequest("res://assets/animals/impala.glb");
// Later:
if (ResourceLoader.LoadThreadedGetStatus(path) == ResourceLoader.ThreadLoadStatus.Loaded) {
    var mesh = ResourceLoader.LoadThreadedGet(path) as Mesh;
}
```

---

## 10. Common Godot 4.6 C# Pitfalls

| Pitfall | Symptom | Fix |
|---------|---------|-----|
| `GetNode()` before `_Ready()` | NullReferenceException | Use `_Ready()` or `CallDeferred` |
| Calling node APIs from Thread | Random crashes | Marshal via `CallDeferred` |
| Setting `InstanceCount` before `Mesh` | No instances render | Assign `Mesh` first |
| Forgetting `QueueFree()` on temporary nodes | Memory leak | Free GLB instances after mesh extraction |
| Using `EmitSignal` on background thread | Crash | Defer signal emission to main thread |
| `_Process()` every frame for simulation | Frame budget blown | Use TimeSystem tick instead |
| Connecting same signal twice | Handler fires twice | Check `IsConnected()` or use one-time `CONNECT_ONE_SHOT` |
| `GD.Print()` from thread | Crash or garbled output | Use `Console.WriteLine()` on threads |

---

## 11. GDScript ↔ C# Interop

C# classes that GDScript needs to access must:
- Inherit from `GodotObject` or `Node`
- Use `[Export]` for properties GDScript sets in Inspector
- Use `[Signal]` for signals GDScript connects to
- Have `public` visibility on methods GDScript calls

```csharp
// C# system that GDScript can read from
public partial class EconomySystem : Node {
    [Export] public float CurrentBalance { get; private set; }

    public float GetSpeciesValue(string speciesId) { ... }
}
```

In GDScript:
```gdscript
var balance = EconomySystem.instance.CurrentBalance  # direct property
var val = EconomySystem.instance.GetSpeciesValue("impala")  # method call
```
