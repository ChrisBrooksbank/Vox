# BUILD MODE

You are in build mode. Your job is to implement ONE task from the plan, then exit.

## 0a. Read AGENTS.md

Read AGENTS.md to understand build/test/lint commands for this project.

## 0b. Read Implementation Plan

Read IMPLEMENTATION_PLAN.md. Find the first uncompleted task (marked with `- [ ]`).

## 0c. Study Relevant Specs

Read the specification file(s) referenced in the task (in specs/ directory) to understand requirements.

## 0d. Understand Existing Code

Read relevant existing code to understand patterns and conventions. Also read CLAUDE.md for project conventions.

## 1. Implement the Task

Write code to complete the task:
- Follow existing code patterns and conventions from CLAUDE.md
- Target framework is `net9.0-windows` for all projects
- Use `Microsoft.Extensions.Hosting` for DI
- Use `System.Threading.Channels` for inter-component communication
- Write tests for new functionality (xUnit in tests/Vox.Core.Tests/)
- Keep changes focused on the single task

## 2. Validate

Run the validation command from AGENTS.md:

```bash
dotnet build && dotnet test
```

If validation fails:
- Fix the issues
- Run validation again
- Repeat until passing

## 3. Update Plan and Exit

After validation passes:
1. Mark the task complete in IMPLEMENTATION_PLAN.md: `- [ ]` becomes `- [x]`
2. Increment the "Build iterations" count in IMPLEMENTATION_PLAN.md
3. Update the "Last updated" date
4. Exit cleanly

The loop will restart with fresh context for the next task.

---

## 99999. GUARDRAILS - READ CAREFULLY

- **DON'T skip validation** - always run `dotnet build && dotnet test` before finishing
- **DON'T implement multiple tasks** - one task per iteration
- **DON'T modify unrelated code** - stay focused on the current task
- **DO follow CLAUDE.md conventions** - especially UIA on STA thread, keyboard hook < 1ms, Channel-based pipeline
- **DO write tests** for new functionality
- **DO update IMPLEMENTATION_PLAN.md** before exiting
- **DO use `net9.0-windows`** as the target framework for all projects
