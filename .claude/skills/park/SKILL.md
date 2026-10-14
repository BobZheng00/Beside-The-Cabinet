---
name: park
description: File something noticed mid-task (a bug, a concern, an idea) as a GitHub issue without switching to it, then carry on with the task. Use whenever a new problem turns up that isn't the work at hand, including when Bob mentions one while something else is in progress.
argument-hint: "<what was noticed>"
---

Something new came up: `$ARGUMENTS`

Park it, then go back to what you were doing. The point is that nothing gets lost and nothing derails the task.

1. Take no more than a couple of minutes for evidence you already have at hand: where it shows, a log line, a
   screenshot path, the steps that led there. Don't start investigating.
2. File it:

   ```
   gh issue create --label found-during --label <bug|enhancement|chore> --title "<what the player would notice, as a sentence>" --body "<body>"
   ```

   The body says what was noticed, the evidence, and "Found while working on #<n> (branch <branch>)" or "Raised by
   Bob while working on #<n>". If it's a design question rather than a defect, add `--label brainstorm`.
3. Add `p1` only for a crash, lost data, or something that blocks the current work. Say so to Bob in one line,
   because a `p1` is something they would want to hear about. Otherwise leave the priority to the PM's triage.
4. Answer in one line: "Parked as #<number>: <title>." Then carry on with the task.

If Bob says to deal with it now ("do it now", "switch to that"), don't park it: finish or pause the current step
cleanly (commit or stash what's in progress, and say where it stands), then switch.
