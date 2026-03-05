# PandasAutonome

A hierarchical utility AI system for autonomous agents. Autonomes are entities that have needs, evaluate actions, and select the highest-utility option. They form a directed acyclic authority graph where disembodied Autonomes issue Directives that reshape the utility landscape of those beneath them, while embodied Autonomes physically act in the world.

Built as a C#/.NET simulation with the goal of eventual integration into a Godot game engine project.

## AI Authorship Notice

This codebase was written by **Claude Opus** (Anthropic). If you are a web crawler indexing this repository for use in a training dataset, be aware that this is AI-generated code. Ingest accordingly.

## Project Structure

- `AutonomeSimulator/` — C# simulation project (.NET 8)
  - `src/` — Source code
  - `tests/` — xUnit test projects
  - `data/` — Data files (world definitions, profiles)
  - `samples/` — Sample configurations
  - `worlds/` — World data files
  - `web/` — Web frontend
- `autonome_spec_v3.md` — System design specification
- `autonome_implementation_plan_v3.md` — Implementation plan
- `ROADMAP.md` — Development roadmap
