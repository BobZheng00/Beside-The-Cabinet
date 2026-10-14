---
name: brainstorm
description: A design session with Bob on a feature or a problem - frame it, interview them on the hard choices, write the spec to docs/specs in the house style, and break it into backlog issues the PM can build. Use for /brainstorm, or for an issue labelled brainstorm.
argument-hint: "<topic, or an issue number>"
disable-model-invocation: true
---

# A design session: $ARGUMENTS

This is the part of the work Bob wants to be in. Make their time count: come prepared, ask about what's hard, and
leave behind a spec and issues the PM can build without coming back to them.

## 1. Prepare (before asking anything)

- If it's an issue, `gh issue view <n> --comments`.
- Read what already bears on it: `docs/roadmap.md`, the related specs in `docs/specs`, `docs/research`, and the code
  it would touch. Many questions are already answered there; don't ask them again.
- Where the world matters (a library, an API, what other apps do), research it. Results end with a "Sources:" list.
- Write a framing of one screen at most: what the player would get, what exists already, the two or three choices
  that decide the design, and what's out of scope.

## 2. Talk it through

- Show the framing, then ask with AskUserQuestion: two to four questions a round, the option you'd recommend first,
  each option saying what it gives and costs. Skip the obvious; dig into the hard parts: edge cases, what the player
  sees when it goes wrong, numbers, privacy, what it costs in tokens or money, how it fits the rules they set (soft
  enforcement, asking before acting outside the app, her manner from her card).
- Go round again on what their answers open up. Stop when the choices that decide the design are made, not when
  every detail is.
- Their words are decisions: quote the ones that matter, with the date, as the specs do.

## 3. Write it down

- The spec goes to `docs/specs/<yyyy-mm-dd>-<slug>.md`, in the shape of the others (`2026-10-09-a-word-first.md`
  is a good model): an italic line saying when it was decided with the user; **Why**; what she does or what the
  player gets; **Decided**; **Not built, and why** or open questions; **Order**. Plain words, British spelling,
  "she" for the companion in these notes.
- Add it to the list of specs in CLAUDE.md's "Where things are", and to `docs/roadmap.md`'s current section.

## 4. Break it into issues

- One issue per step of the **Order** that can be built and checked on its own. Each with a title saying what the
  player gets, a body linking the spec section, and a **Done when** list that QA can check from screenshots, logs
  and tests. Labels: kind, priority, and `ready`, or `decision` where a choice was left open on purpose.
- Close or relabel the issue the session started from, with a link to the spec and the new issues.
- Commit the spec and roadmap change on a branch (`spec-<slug>`) and open a draft pull request for it. It goes
  through the PM's QA like anything else; Markdown-only changes skip the smoke suite.

End with: the spec's path, the issues made (numbers and titles), and what's left open.
