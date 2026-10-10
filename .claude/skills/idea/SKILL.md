---
name: idea
description: Thinking partner before /design or /fix - interviews, challenges and improves a loose idea or bug report, then saves a ready prompt file that `/design idea:<slug>` or `/fix idea:<slug>` reads. Writes no spec, no issue, no code.
argument-hint: "<loose idea or bug description | slug of a saved prompt>"
disable-model-invocation: true
model: claude-opus-5-5
effort: high
---

Shape `$ARGUMENTS` into one prompt file per coherent change, good enough that `/design` (or `/fix`)
starts from settled product decisions instead of a loose paragraph. You decide **what and why** with
the user; **how** stays with `/design`. Talk to the user in Polish. Write the prompt in Polish, with
code identifiers, routes and type names in English.

## 0. Start

- No arguments → `node scripts/idea.mjs list`, then `AskUserQuestion`: continue one of the drafts, or
  a new idea (then ask for it).
- Arguments equal to a listed slug → `node scripts/idea.mjs show <slug>` and refine that prompt: steps
  2–7 on the existing file, never a second file for the same idea.

## 1. Context

Run, with the arguments verbatim in a quoted heredoc so quotes and URLs survive:

```
node scripts/idea.mjs context --out <scratchpad>/idea-context.md -- "$(cat <<'IDEA'
<arguments>
IDEA
)"
```

Read its output: app map (slices, routes), board (open and done issues), ideas.md, ADR titles, product "Out of
scope", saved prompts, and "Possibly related" matches. That is your map. Never read
`architecture.md` or `domain.md` wholesale — at most the one section a question needs.

## 2. Triage — at most 5 lines to the user

- How you understood the goal, in one sentence.
- Kind: **new**, **change** (existing behaviour should differ), **cleanup**, or **bug** (code fails its
  own intent → target `fix`).
- An **open** issue already covers it → the prompt becomes an amendment request for `#n`. A **done**
  issue built it → this is a change of that issue's behaviour.
- Several ideas in one input → one prompt file each; ask which comes first and finish it before the next.
- A conflict with product "Out of scope", an ADR or a hard rule (no investment advice, money in PLN,
  tenancy, …) → say so plainly, with the ADR number.

A pure question ("dlaczego…?", "czym jest…?") → answer it briefly from the map, or from the one
`Explore` call (step 4), then ask whether a change or a bug sits behind it. None → stop.

## 3. Interview

No question quota. The filter: **ask only what changes the prompt, and only at product level** —
behaviour, screens and routes, concrete examples with amounts and dates, edge cases, non-goals,
priority and MVP. Never ask how to implement it: every technical choice or code fact goes under
"Otwarte pytania dla /design".

`AskUserQuestion`, 2–4 questions per call, one topic each, your recommended option first. Be a critical,
creative partner, not a stenographer:
- What problem does it solve? Is there a simpler alternative that solves most of it?
- MVP vs the full version — propose the cut.
- Adjacent value: a related line from ideas.md or a done issue it could build on.
- Propose deferring or dropping it when it does not pay off; propose a split into an epic when it is big.
- Offer your own proposals as options labelled `Pomysł: …`.

For a bug: steps, URL, data used, expected vs actual, since when.

## 4. One fact from the code

At most **one** `Explore` subagent call, for a single fact the map cannot settle and that changes the
prompt ("does X exist?", "where does the dashboard get Y from?"). No other code reading.

## 5. Write the prompt

`node scripts/idea.mjs path <slug>` gives the file path (kebab-case slug). Write it with `Write` from
`.claude/skills/idea/prompt-template.md` (the design or the fix template), `status: draft` while
iterating, the template's HTML comments removed. Principles:
- what and why, not how; one prompt = one coherent change; ≤ ~60 lines; no code, no code fences;
- concrete examples (amounts in PLN, dates, before → after);
- under "Decyzje już podjęte" mark each item as **wymaganie** (requirement) or **sugestia** (suggestion);
- an amendment of `#n` names it in the title line and under "Powiązane".

## 6. Critic

Spawn the `idea-critic` agent with the prompt path and `<scratchpad>/idea-context.md`. Findings that
need the user's decision → one `AskUserQuestion` round; fix the rest yourself. Never show the raw
critic output.

## 7. Finish

`node scripts/idea.mjs mark <slug> ready`, then `node scripts/idea.mjs check <slug>`; fix what it lists
until its `IDEA_RESULT` says `ok`. Final report, at most 10 lines, in Polish: goal, kind → command, key
decisions, open questions for /design, the path, "w schowku" when `clipboard` is true, and the next
step: `/clear`, then `/design idea:<slug>` (or `/fix idea:<slug>`; an amendment of `#n`: `/design`
with `idea:<slug>`, which amends `#n`).

## Hard constraints

- The prompt file is the only thing you write. No spec, no issue, no code, no other repo file.
- No git command that changes state.
