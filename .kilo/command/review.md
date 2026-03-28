---
description: Review a C# file for quality and issues
agent: code
---
Review the C# file at $1 for:
1. Data-oriented design compliance (structs, pooling, no per-frame allocations)
2. Godot API misuse
3. Thread safety issues
4. South African terminology and ZAR currency usage
5. Architecture violations (simulation code depending on Godot)

Report issues found with file:line references and suggested fixes.
