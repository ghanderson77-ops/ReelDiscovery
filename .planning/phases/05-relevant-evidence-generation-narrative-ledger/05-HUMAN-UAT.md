---
status: partial
phase: 05-relevant-evidence-generation-narrative-ledger
source: [05-VERIFICATION.md, 05-17-PLAN.md]
started: 2026-10-03T00:00:00Z
updated: 2026-10-03T00:00:00Z
---

## Current Test

[awaiting human testing]

## Tests

### 1. Live provider run, narrative coherence across waves
Open a case with an approved plan, go to Generate Corpus, click Generate Emails and let at least 2 waves finish.
expected: Later threads refer back to commitments, figures, meetings and people introduced earlier; no email mentions a fact dated after it or known only to people not on it.
result: [pending]

### 2. Reading quality of relevant emails
Read ~10 relevant threads (Indirect / Clear / Explicit, hot, privileged counsel threads, several senders).
expected: Natural business voice; varied subtlety (mostly indirect, some clear, a few explicit); hot documents state the key point plainly; counsel threads give plain legal advice with banner/label where assigned; distinct voices and cast signatures; no legal framing ("plaintiff", "alleged") outside counsel threads.
result: [pending]

### 3. EMLs in a mail client
Open a reply, a forward, a privileged thread and an external-sender email from Mailboxes/{custodian}/Inbox and Sent Items in Outlook / Thunderbird / Windows Mail.
expected: Messages group as one conversation; quoting shows the right parent; RE:/FW: not doubled; themed HTML; Date shows sender-local time; X-ReelDiscovery-DocID header (view source) matches answer-key.csv.
result: [pending]

### 4. Responsiveness, Cancel, Continue and kill
During the live run drag/resize the window; press Cancel Generation mid-wave, then Continue Generating; separately end the process in Task Manager mid-run and reopen the case.
expected: Window never freezes; tiles and header cost update live; after Cancel or kill every finished thread's EMLs and answer-key rows remain; Continue writes only the remaining threads.
result: [pending]

### 5. Visuals at 150% DPI
Run the app with Windows scaling at 150% and view Generate Corpus states, Case Summary Mailboxes / Story so far rows and the re-plan dialog. (Automated renders on this machine ran at 120 DPI.)
expected: No clipped controls, no ellipsized button text, no stock grey surfaces, no horizontal grid scrollbars.
result: [pending]

### 6. Story So Far document
On Case Summary click Open in Word on the Story so far row.
expected: Ledger grouped by month; readable statements with date, kind, value, who knew it, facts, issues and source DocID; warnings section only when threads were kept with warnings.
result: [pending]

## Summary

total: 6
passed: 0
issues: 0
pending: 6
skipped: 0
blocked: 0

## Gaps
