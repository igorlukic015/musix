Use the `tester` subagent to implement integration tests for newly written logic in this project.

## What to hand to the tester agent

1. Run `git diff HEAD` and `git status` to identify `.cs` files added or modified under `Musix/` (not `Musix.Tests/`).
2. Pass the list of changed files and their contents to the `tester` agent along with this instruction: write meaningful tests covering the core logic and edge cases of the changed code, following the project conventions already established in `Musix.Tests/`.

The tester agent handles everything from there — reading the code, identifying scenarios, writing the tests, and running them.
