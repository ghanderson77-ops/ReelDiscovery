---
status: investigating
trigger: "A generated personal email shows From: A friend <a.friend@gmail.com>. The Office case plan has personal externals 'A friend' (a.friend@gmail.com), 'A neighbor' (a.neighbor@outlook.com), 'A friend' (a.friend2@gmail.com)."
created: 2026-10-05
updated: 2026-10-05
---

## Current Focus

hypothesis: SubjectSeeder.RenameExternal accepts any 2-token model senderName for Personal externals, and the senderName DTO description ("the sending publication, company or person") invites a role, so "A friend" becomes first "A", last "friend"
test: read RenameExternal + DTO description; inspect user's plan (read only) and journal for written threads from EXT-008..010
expecting: plan externals carry role names; journal ThreadDone From fields show the role name
next_action: implement LooksLikePersonName, tighten prompt, add journal-aware load repair

## Symptoms

expected: personal mail comes from a realistic invented person (e.g. "Dana Holloway <dana.holloway@gmail.com>")
actual: From: A friend <a.friend@gmail.com>; plan externals EXT-008 "A friend", EXT-009 "A neighbor", EXT-010 "A friend" (a.friend2@gmail.com)
errors: none (content quality)
reproduction: plan an Office-inspired case with personal mail; model returns a role for senderName
started: since Phase 4 subject seeding (04-14) renamed externals from model output

## Eliminated

## Evidence

- timestamp: 2026-10-05
  checked: Services/Corpus/SubjectSeeder.cs RenameExternal personal branch
  found: only requires >=2 whitespace tokens whose first and last fold to non-empty [a-z0-9]; "A friend" passes -> AddressFor("A","friend") = a.friend@gmail.com; second "A friend" collides -> a.friend2@gmail.com
  implication: exact mechanism for all three observed identities
- timestamp: 2026-10-05
  checked: SubjectSeedDto.SenderName description and SeedTaskText
  found: "For external slots: the sending publication, company or person" / "also give the sending publication, company or person"; slot line only says "external: personal"
  implication: prompt asks for a description of the sender, not a name; the model answered with a role
- timestamp: 2026-10-05
  checked: user's corpus-plan.json externals (read only)
  found: business externals are also generic descriptors ("Books newsletter", "Office supplies vendor", "Jewelry promotions") -> same prompt weakness for business senders, but they are at least plausible display names
  implication: prompt wording should ask for an invented realistic name for every kind
- timestamp: 2026-10-05
  checked: user's generation-journal.jsonl vs plan threads involving EXT-008..010
  found: 42 threads involve those externals; all 42 have ThreadDone; ThreadDone Messages[].From records "A friend <a.friend@gmail.com>"
  implication: every personal thread is already written with the role name; renaming on load would make answer-key Display (plan.Externals Name/Email) and future renders disagree with the written EMLs
- timestamp: 2026-10-05
  checked: AnswerKeyBuilder.Lookups, PeopleDirectory.Create, GenerationRunner checkpoint
  found: answer key rows and every render read the sender identity from plan.Externals; the key is re-projected from the journal + plan at every checkpoint; only reset is Start Over (whole run) or re-plan
  implication: repair must skip externals that already appear (by original address) in a ThreadDone From of this plan; the lock check keyed on the ORIGINAL address keeps the decision stable after threads are written with the repaired name
- timestamp: 2026-10-05
  checked: EvidencePrompts.PersonLine and the written EML
  found: written personal EML body ends with "Full"; journal SignOff = "Full"; PersonLine prints "signs off: {member.SignatureStyle}" (enum value Full) for cast lines
  implication: separate bug (signature style enum leaks into the prompt as a sign-off word); reported, not fixed here

## Resolution

root_cause:
fix:
verification:
files_changed: []
