# Phase 7: Scale, Cost, QA & Cutover - Discussion Log

**Date:** 2026-10-05
**Areas discussed:** Batch run behavior, QA passes, Cost display & pricing, Legacy cutover

> Human reference only. The decisions themselves are in 07-CONTEXT.md.

## Batch run behavior
| Question | Options | Selected |
|---|---|---|
| What goes through the Batch API | Everything wave by wave / Non-relevant + attachments only / Per stage | Everything wave by wave, but recommend it for very large corpora only |
| How batch mode is turned on | Per run on Ready card / Global / Automatic above a size | Per run, on the Ready card |
| While batches are pending | Free to close the app / Stay on screen, polling | Stay on screen, polling |
| Failed or expired items | Re-run live / Re-batch / Leave for Retry Failed | Re-run live automatically |

## QA passes
| Question | Options | Selected |
|---|---|---|
| Blind-review scope | Sample, user sets size / Whole corpus | Sample, user sets size |
| On disagreement | Flag, user chooses per item / Auto-regenerate / Report only | Report only |
| QA-01 conflict (relabel/regenerate) | Report plus one manual action / Amend QA-01 | Report, plus a manual "Regenerate thread" action |
| Reviewer model | Different provider / Story tier / Volume tier | A different provider than wrote it |
| Leakage audit | Report + top signals / Report + suggest re-plan | Report + top signals |
| Timeline/value scans | Auto after each run, report only / Auto, block export | Auto after each run, report only |

## Cost display & pricing
| Question | Options | Selected |
|---|---|---|
| Cost view | One total + breakdown tooltip / Always-visible savings line | One total + breakdown tooltip |
| Where to edit pricing | Extra columns in model dialog / Sub-dialog | Extra columns in the model dialog |

## Legacy cutover
| Question | Options | Selected |
|---|---|---|
| How much to remove | All of it / UI only | All of it |
| Startup | API setup then case list / Straight to cases | API setup, then the case list |
| Old settings files | Migrate silently / Ignore | Ignore them |

## Phase 6 deferred items
Folded into Phase 7: privilege incidence, invite times across time zones, Start Over delete retry, and theme font cleaning.

## Claude's discretion
Batch-recommendation threshold, batch packaging and polling, leakage classifier and thresholds, blind-review prompt and sampling, 5K memory approach.
