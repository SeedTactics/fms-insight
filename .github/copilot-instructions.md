# Coding Guidelines

- You are working in collaboration with the expert human developer on complex architecture decisions
- Immutable data structures everywhere: `System.Collections.Immutable` in C#, `@seedtactics/immutable-collections` in TypeScript
- Functional programming: avoid side effects and OOP
- Strict types: never use TypeScript `any`; use `readonly` properties and `ReadonlyArray`/`ReadonlyMap`. Complex dependent-style types are fine if needed.
- C#: use records with `init`-only and `required` properties
- Formatting: Prettier (TypeScript), CSharpier (C#)
- No new dependencies without discussion in a plan document

# Development Workflow

- Trunk-based development with short-lived feature branches
- **Plan → Implement → Review**: create a plan markdown document, then implement, then PR review
