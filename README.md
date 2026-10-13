# ReelDiscovery

**AI-Powered E-Discovery Sample Data Generator**

*A free tool from [QuikData](https://www.quikdata.com) — Scalable On-Prem, SaaS, and Hybrid E-Discovery solutions for law firms, service providers, and corporate/government. AI Powered.*

---

ReelDiscovery builds realistic, internally consistent eDiscovery data sets for training, testing and demonstrations. You describe a dispute or investigation in plain English. ReelDiscovery writes the legal document, builds a case guide from it, and generates up to ~5,000 emails with attachments. The relevant emails are the evidence behind the allegations, mixed with a share of ordinary business traffic you control, plus an exact answer key saying which documents are relevant and why.

It works with **OpenAI** or **Anthropic Claude**.

> **New in 2.0:** ReelDiscovery was rebuilt around a case guide. The 1.x topic → storylines → characters wizard is gone. See the [CHANGELOG](CHANGELOG.md).

## How it works

1. **Describe the Case.** Write a scenario, such as *"construction defect dispute between a window subcontractor, a general contractor and the developer on a midrise commercial project"*, and pick a Simple, Standard or Complex preset.
2. **Review Core Document.** ReelDiscovery writes a Complaint, Petition or Investigation Memo. Edit it, regenerate sections, and fix any flagged issues.
3. **Case Guide.** The document becomes a structured guide: issues, a cast of key characters, a dated timeline of key facts, and canonical values (amounts, dates, names). Everything later is generated from this guide, so edit it here.
4. **Corpus Settings.** Choose the corpus size, the relevant share, the non-relevant mix, the date window and the attachment types.
5. **Review Plan.** Before any email text is written, code plans every thread and message: dates, senders and recipients, replies, which facts each email covers, labels and attachments. Review the plan and its cost estimate, then approve it.
6. **Generate Corpus.** Emails are written in date order. Each one knows only what its participants could know at that point, and later emails build on what earlier ones said. Finished threads are saved as they complete, so you can cancel and continue later.
7. **Discovery Documents (optional).** Generate deposition excerpts, Requests for Production, Requests for Admission and Interrogatories that match the case and cite emails from the corpus as exhibits.
8. **Case Summary.** Open the mailboxes, answer key, review protocol and QA findings; export the case guide to Word.

## Features

### Case-driven evidence
- **One consistent story.** Relevant emails are tied to specific dated facts and characters, so a 5,000-email corpus reads like the evidence behind one case.
- **Realistic mix of non-relevant mail.** The same custodians' daily business, other people in their orbit, newsletters, spam and personal mail, and **near-miss decoys** that share the project and keywords but not the alleged conduct.
- **Hot and privileged documents**, flagged in the answer key.
- **Realistic threading** with Message-ID, In-Reply-To and References headers, replies and forwards, business hours, time zones and holidays.

### Answer key and QA
- **Answer key** in CSV and Excel: one row per email and attachment, relevance with the reason, family relevance, Hot / Privileged / Near-miss flags, and statistics and legend sheets.
- **Review protocol** in Word, written from the same plan.
- **Manual relabels** you make in the answer key are kept when the corpus is continued, a thread is regenerated, or the run is started over.
- **QA** on Case Summary: a blind-review sample, a label-leakage audit, and timeline and value consistency checks.

### Attachments
- **Word, Excel and PowerPoint** documents with per-organization branding.
- **Calendar invites** (.ics).
- **Images** (gpt-image), inline or attached.
- **Voicemails** (text-to-speech MP3).

### Scale and cost
- Up to **5,000 emails** per corpus.
- **Batch APIs** (OpenAI and Anthropic) and prompt caching for lower cost; batches are tracked on disk and collected after a restart.
- Running cost shown during the run; a rough estimate before you start.
- **Nothing finished is lost.** One failed item never stops the run; cancel, close or crash and continue later.

## Requirements

- Windows 10/11 (64-bit)
- An API key for **OpenAI** or **Anthropic**. Images and voicemails use OpenAI, so they need an OpenAI key.

## Installation

### Option 1: Download a release
Download the latest `ReelDiscovery.exe` from the [Releases](https://github.com/ghanderson77-ops/ReelDiscovery/releases) page. No installation required; just run it.

### Option 2: Build from source
Requires the .NET 10 SDK.

```bash
git clone https://github.com/ghanderson77-ops/ReelDiscovery.git
```

```bash
cd ReelDiscovery
```

```bash
dotnet run --project ReelDiscovery.csproj
```

Run the tests:

```bash
dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj
```

## Corpus settings

| Setting | Default | Range |
|---------|---------|-------|
| Total emails | 1,000 | 50 – 5,000 (presets 250 / 1,000 / 2,500 / 5,000) |
| Relevant share | 20% | 5% – 60% |
| Non-relevant mix | 50% daily business, 20% ancillary, 20% newsletter/spam/personal, 10% near-miss decoys | adjustable |
| Attachment types | Office, calendar, images, voicemail | each can be turned off |

## Output

Each case gets its own folder (by default under `Documents\ReelDiscovery\Cases`):

```
My Case/
├── case.json                         ← the case and its guide
├── corpus-plan.json                  ← the approved plan
├── answer-key.csv
├── answer-key.xlsx
├── My Case - Review Protocol.docx
├── My Case - Core Document.docx      ← case guide exports
├── My Case - Cast of Key Characters.docx
├── My Case - Key Facts & Events.docx
├── Mailboxes/
│   ├── <custodian>/
│   │   ├── Inbox/<DocID>.eml
│   │   └── Sent Items/<DocID>.eml
│   └── ...
└── Discovery/                        ← optional discovery documents
```

Every email carries an `X-ReelDiscovery-DocID` header that matches its answer-key row. The `.eml` files can be loaded into Outlook, Relativity, Nuix, Concordance and most eDiscovery platforms.

## Example scenarios

- "Construction defect dispute between a window subcontractor, a general contractor and the developer on a midrise commercial project"
- "Trade-secret theft by a departing sales director who joins a competitor"
- "Hospital billing fraud investigation into upcoding by a physician group"
- "Shareholder dispute over a family-owned manufacturing company"
- "Correspondence inspired by a TV crime drama evidencing a criminal conspiracy" (names are swapped for fictional ones)

## Disclaimer

All generated content is fictional and created by AI for demonstration purposes only. Generated emails include a disclaimer banner. This tool is intended for:
- E-discovery software training
- Legal technology demonstrations
- Educational purposes
- Testing and development

Do not use generated content to mislead or deceive.

## Privacy

- **No telemetry.** ReelDiscovery 2.0 sends no usage data to QuikData.
- Your scenario and generated text go only to the AI provider you choose (OpenAI or Anthropic), under that provider's terms.
- API keys are stored on your PC, encrypted with Windows DPAPI (`%LOCALAPPDATA%\ReelDiscovery`).
- Cases and corpora stay in the local folder you choose.

## Tech stack

- .NET 10 / Windows Forms, single self-contained executable
- OpenAI and Anthropic APIs (text, Batch APIs); OpenAI gpt-image and TTS for media
- MimeKit (email)
- DocumentFormat.OpenXml (Office documents)

## About QuikData

ReelDiscovery is brought to you by **[QuikData](https://www.quikdata.com)** — a leading provider of AI-powered e-discovery solutions.

**QuikData offers:**
- **On-Premises** - Full control with deployment in your own data center
- **SaaS** - Cloud-hosted solution with no infrastructure to manage
- **Hybrid** - Flexible deployment combining on-prem processing with cloud review

**Built for:**
- Law Firms
- Legal Service Providers
- Corporate Legal Departments
- Government Agencies

[Learn more at quikdata.com](https://quikdata.com)

## License

MIT License - See [LICENSE](LICENSE) for details.

## Contributing

Contributions welcome! Please open an issue to discuss proposed changes before submitting a PR.

## Acknowledgments

- Built with [OpenAI](https://openai.com) and [Anthropic](https://www.anthropic.com) APIs
- Email handling by [MimeKit](https://github.com/jstedfast/MimeKit)
- Office documents by [Open XML SDK](https://github.com/OfficeDev/Open-XML-SDK)
- Created by [QuikData](https://quikdata.com)
