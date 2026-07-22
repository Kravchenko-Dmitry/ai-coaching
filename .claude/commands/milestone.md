Implement milestone $ARGUMENTS from CLAUDE.md.

Steps:
1. Re-read SPEC.md sections relevant to this milestone. List them.
2. State in 2–3 sentences what you will build and any assumptions.
3. Wait for my confirmation only if something is ambiguous vs SPEC.md; otherwise proceed.
4. Implement, then run `dotnet build && dotnet test`.
5. If green: summarize files changed and propose commit message `M$ARGUMENTS: <summary>`. Do not commit yourself.
6. If red: fix before reporting done. Never weaken or delete a failing test to make it pass.