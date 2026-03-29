Commit all current changes as a series of small, logical commits. Each commit must tell a clear story so the history reads like a changelog a developer can follow.

## Step 1 — Understand the full diff

Run these together to get the complete picture:
```
git status
git diff
git diff --cached
```

Read every changed file in full before deciding how to group anything.

## Step 2 — Plan the commits

Group changes into cohesive logical units. A good commit contains one reason to change. Ask for each file or chunk: *what problem does this solve or what step does this represent?*

Good grouping examples:
- All interop type definitions together (they change for the same reason)
- A new class and its direct dependencies together
- A bug fix isolated from unrelated refactoring
- Tests for a class together with that class if they were written simultaneously
- Rename/move operations as their own commit (keeps diffs readable)

Bad grouping:
- Mixing a bug fix with a refactor in one commit
- One giant commit for everything
- Splitting a class and its tests into separate commits if they were written as a unit

## Step 3 — Commit each group

For each group, stage only the relevant files or hunks and commit immediately before moving to the next group. Use `git add <specific files>` — do not use `git add .` or `git add -A`.

You may freely run any git command that is read-only or additive: `git status`, `git diff`, `git log`, `git show`, `git add`, `git commit`, `git stash list`, etc. Do **not** run commands that discard or rewrite working-tree or history state: `git reset`, `git checkout -- <file>`, `git restore`, `git clean`, `git stash drop/pop/clear`, `git rebase`, `git push --force`, or any variant with `--hard` / `--force`.

Commit message format:
- First line: imperative mood, max 72 chars, no period — describes *what* the commit does
- If the reason is non-obvious, add a blank line then a short body explaining *why*

Good messages:
```
Add WASAPI process loopback activation via ActivateAudioInterfaceAsync
Extract session discovery into AudioSessionEnumerator
Fix GetMixFormat E_NOTIMPL by reading format from default render endpoint
Replace CompletionHandler parameter with IntPtr to avoid CLR QI marshaling
Add integration tests for ProcessLoopbackCapture lifecycle
```

Bad messages:
```
fix
update files
wip
changes
```

## Step 4 — Verify

After all commits, run `git log --oneline -20` and read the result. The sequence of messages should read as a coherent narrative of the work done. If any message is vague or two commits could be merged without losing meaning, that is a signal the grouping needs adjustment — but do not amend already-created commits; note it for the user instead.
