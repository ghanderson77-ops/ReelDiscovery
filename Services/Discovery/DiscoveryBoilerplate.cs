using System.Globalization;
using System.Text;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Code-owned text of the written discovery sets and transcript captions (D-04, D-17, D-20): captions, preliminary
/// statement, definitions, instructions, request labels, signature block, certificate of service and service date.
/// Federal sets cite the Federal Rules of Civil Procedure; state sets and memo variants cite no rule or statute number.
/// The model only writes request bodies.
/// </summary>
public static class DiscoveryBoilerplate
{
    // Days from filing to service of the first written discovery sets
    private const int ServiceLagDays = 45;

    private const string BlankNumber = "____________";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Caption for a written discovery set: the core document caption with the assigned case number and the set title
    /// (pleadings), or a matter header for the memo variants (D-25). The last non-blank line is always the title.
    /// </summary>
    public static string CaptionText(CoreDocument doc, string caseNumber, string title)
    {
        var number = string.IsNullOrWhiteSpace(caseNumber) ? BlankNumber : caseNumber.Trim();
        if (!DiscoveryVariants.IsMemo(DiscoveryVariants.Of(doc)))
            return CoreDocumentAssembler.BuildCaption(doc, number, title);

        var sb = new StringBuilder();
        foreach (var line in MemoCaptionLines(doc, number))
            sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine(title);
        return sb.ToString();
    }

    /// <summary>
    /// Caption lines for a deposition cover page: the pleading caption without its title line, or the memo matter header.
    /// </summary>
    public static List<string> TranscriptCaptionLines(CoreDocument doc, string caseNumber)
    {
        var number = string.IsNullOrWhiteSpace(caseNumber) ? BlankNumber : caseNumber.Trim();
        if (DiscoveryVariants.IsMemo(DiscoveryVariants.Of(doc)))
            return MemoCaptionLines(doc, number);

        var lines = SplitLines(CoreDocumentAssembler.BuildCaption(doc, number, string.Empty));
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>
    /// First business day on or after FilingDate + 45 days (TimelineEnd + 45 days without a filing date), yyyy-MM-dd.
    /// Empty when neither date parses.
    /// </summary>
    public static string ServiceDate(CaseDraft draft)
    {
        var doc = draft?.CoreDocument;
        if (!CaseDraft.TryParseDate(doc?.FilingDate, out var anchor) && !CaseDraft.TryParseDate(doc?.TimelineEnd, out anchor))
            return string.Empty;

        var day = BusinessCalendar.NextBusinessDay(DateOnly.FromDateTime(anchor).AddDays(ServiceLagDays));
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string PreliminaryStatement(CoreDocument doc, DiscoveryKind kind, CaseParty propounding, CaseParty responding)
    {
        var from = PartyRef(doc, propounding);
        var to = PartyRef(doc, responding);
        var toName = Name(responding);
        var fromName = Name(propounding);

        switch (DiscoveryVariants.Of(doc))
        {
            case DiscoveryVariant.MemoCriminal:
                return $"This rider is part of the subpoena served on {toName}. Produce the documents described below that are in "
                    + "your possession, custody or control, in accordance with the definitions and instructions that follow, by the "
                    + "return date stated in the subpoena.";

            case DiscoveryVariant.MemoRegulatory:
                return kind == DiscoveryKind.Interrogatories
                    ? $"This Civil Investigative Demand requires {toName} to answer each interrogatory below separately and fully, in "
                        + "writing and under oath, in accordance with the definitions and instructions that follow, by the return date "
                        + "stated in the demand."
                    : $"This Civil Investigative Demand requires {toName} to produce the documents described below that are in its "
                        + "possession, custody or control, in accordance with the definitions and instructions that follow, by the "
                        + "return date stated in the demand.";

            case DiscoveryVariant.MemoCivil:
                return kind switch
                {
                    DiscoveryKind.Interrogatories =>
                        $"{fromName} requests that {toName} answer each question below separately and fully, in writing, within "
                        + "thirty (30) days of receipt, in accordance with the definitions and instructions that follow.",
                    DiscoveryKind.RequestsForAdmission =>
                        $"{fromName} requests that {toName} admit or deny each statement below in writing within thirty (30) days "
                        + "of receipt, in accordance with the definitions and instructions that follow.",
                    _ => $"{fromName} requests that {toName} preserve and produce the documents described below that are in its "
                        + "possession, custody or control, within thirty (30) days of receipt, in accordance with the definitions "
                        + "and instructions that follow."
                };
        }

        if (IsState(doc))
        {
            var rules = $"Pursuant to the {StateName(doc)} Rules of Civil Procedure";
            return kind switch
            {
                DiscoveryKind.Interrogatories =>
                    $"{rules}, {from} serves the following Interrogatories on {to}, to be answered separately and fully, in "
                    + "writing and under oath, within thirty (30) days of service.",
                DiscoveryKind.RequestsForAdmission =>
                    $"{rules}, {from} serves the following Requests for Admission on {to}, to be answered in writing within "
                    + "thirty (30) days of service.",
                _ => $"{rules}, {from} requests that {to} produce the documents described below within thirty (30) days of "
                    + "service, in accordance with the definitions and instructions that follow."
            };
        }

        return kind switch
        {
            DiscoveryKind.Interrogatories =>
                $"Pursuant to Rules 26 and 33 of the Federal Rules of Civil Procedure, {from} serves the following Interrogatories "
                + $"on {to}, to be answered separately and fully, in writing and under oath, within thirty (30) days of service.",
            DiscoveryKind.RequestsForAdmission =>
                $"Pursuant to Rules 26 and 36 of the Federal Rules of Civil Procedure, {from} requests that {to} admit the truth "
                + "of each matter set forth below, in writing, within thirty (30) days of service.",
            _ => $"Pursuant to Rules 26 and 34 of the Federal Rules of Civil Procedure, {from} requests that {to} produce the "
                + "documents described below within thirty (30) days of service, in accordance with the definitions and "
                + "instructions that follow."
        };
    }

    public static List<DiscoveryDefinition> Definitions(CaseDraft draft, DiscoveryKind kind, CaseParty responding)
    {
        var doc = draft?.CoreDocument;
        var federal = doc != null && !IsState(doc) && !DiscoveryVariants.IsMemo(DiscoveryVariants.Of(doc));
        var name = Name(responding);
        var shortName = (responding?.ShortName ?? string.Empty).Trim();
        var known = shortName.Length > 0 && !string.Equals(shortName, name, StringComparison.Ordinal)
            ? $" (\"{shortName}\")"
            : string.Empty;

        var definitions = new List<DiscoveryDefinition>
        {
            new()
            {
                Term = "You\" and \"Your",
                Text = $"mean {name}{known}, and its present and former officers, directors, employees, agents, representatives, "
                    + "parents, subsidiaries, affiliates and all other persons acting or purporting to act on its behalf."
            },
            new()
            {
                Term = "Document",
                Text = federal
                    ? "has the full meaning given in Federal Rule of Civil Procedure 34(a)(1)(A) and includes electronically stored "
                        + "information, emails and their attachments, text and instant messages, calendar entries, spreadsheets, "
                        + "drawings, photographs and drafts, whether in paper or electronic form."
                    : "means any written, printed, recorded or electronically stored information of any kind, including emails and "
                        + "their attachments, text and instant messages, calendar entries, spreadsheets, drawings, photographs and "
                        + "drafts, whether in paper or electronic form."
            },
            new()
            {
                Term = "Communication",
                Text = "means any transmission of information by any means, including emails, letters, memoranda, text or instant "
                    + "messages, voicemails, telephone calls, meetings and conversations."
            },
            new()
            {
                Term = "Concerning",
                Text = "means relating to, referring to, describing, evidencing, constituting, reflecting or discussing."
            },
            new()
            {
                Term = "Person",
                Text = "means any natural person, corporation, limited liability company, partnership, association, government "
                    + "agency or other entity."
            },
            new()
            {
                Term = "Identify",
                Text = kind == DiscoveryKind.Interrogatories
                    ? "means, for a natural person, to state the person's full name, employer, title and last known contact "
                        + "information; for a Document, to state its date, author, recipients, type and subject matter."
                    : "means, for a natural person, to state the person's full name, employer and title; for a Document, to state "
                        + "its date, author, recipients and subject matter."
            },
            new()
            {
                Term = "Relevant Period",
                Text = $"means {RelevantPeriod(doc)}."
            }
        };

        // One defined term per canonical project name and contract number, using the guide's own label (D-04)
        var values = (draft?.Guide?.Values ?? new List<CanonicalValue>())
            .Where(v => v != null
                && v.Kind is ValueKind.ProjectName or ValueKind.ContractNumber
                && !string.IsNullOrWhiteSpace(v.Label)
                && !string.IsNullOrWhiteSpace(v.Value));
        foreach (var value in values)
        {
            var label = value.Label.Trim();
            if (definitions.Any(d => string.Equals(d.Term, label, StringComparison.OrdinalIgnoreCase)))
                continue;

            definitions.Add(new DiscoveryDefinition
            {
                Term = label,
                Text = value.Kind == ValueKind.ProjectName
                    ? $"means the project known as {value.Value.Trim()}."
                    : $"means the contract identified as {value.Value.Trim()}, including all amendments, change orders and "
                        + "attachments."
            });
        }

        return definitions;
    }

    public static List<string> Instructions(CoreDocument doc, DiscoveryKind kind)
    {
        var variant = DiscoveryVariants.Of(doc);
        var period = RelevantPeriod(doc);
        var instructions = new List<string>();

        if (DiscoveryVariants.IsMemo(variant))
        {
            var deadline = variant == DiscoveryVariant.MemoCivil
                ? "within thirty (30) days of receipt"
                : $"by the return date stated in the {(variant == DiscoveryVariant.MemoCriminal ? "subpoena" : "demand")}";
            instructions.Add($"Unless a request states otherwise, the Relevant Period is {period}.");
            instructions.Add(kind switch
            {
                DiscoveryKind.Interrogatories => $"Answer each question separately and fully, in writing, {deadline}. Each question is a single question; answer it without subparts.",
                DiscoveryKind.RequestsForAdmission => $"Admit or deny each statement in writing {deadline}. If you cannot admit or deny a statement, explain in detail why.",
                _ => $"Produce the documents {deadline}, organized by the request to which they respond."
            });
            instructions.Add("Produce electronically stored information in native format with all metadata intact, including emails with their attachments and family relationships preserved.");
            instructions.Add("If you withhold any document or information on a claim of privilege, provide a privilege log identifying its date, author, recipients, subject matter and the privilege claimed.");
            instructions.Add("Preserve all documents and electronically stored information concerning the matters described in this request, and suspend any routine deletion that could affect them.");
            instructions.Add("These requests are continuing. If you later locate or obtain additional responsive documents or information, supplement your response promptly.");
            return instructions;
        }

        var state = IsState(doc);
        instructions.Add($"Unless a request states otherwise, the Relevant Period is {period}.");

        if (state)
        {
            var rules = $"the {StateName(doc)} Rules of Civil Procedure";
            instructions.Add(kind switch
            {
                DiscoveryKind.Interrogatories => $"These Interrogatories are served under {rules}. Answer each Interrogatory separately and fully, in writing and under oath, within thirty (30) days of service. Each interrogatory is a single question; answer it without subparts.",
                DiscoveryKind.RequestsForAdmission => $"These Requests for Admission are served under {rules}. Answer each request in writing within thirty (30) days of service. Any matter not timely answered is deemed admitted.",
                _ => $"These Requests for Production are served under {rules}. Serve your written response and produce the documents within thirty (30) days of service."
            });
        }
        else
        {
            instructions.Add(kind switch
            {
                DiscoveryKind.Interrogatories => "These Interrogatories are served under Federal Rules of Civil Procedure 26 and 33. Answer each Interrogatory separately and fully, in writing and under oath, within thirty (30) days of service as required by Rule 33(b). Each interrogatory is a single question; answer it without subparts.",
                DiscoveryKind.RequestsForAdmission => "These Requests for Admission are served under Federal Rules of Civil Procedure 26 and 36. Answer each request in writing within thirty (30) days of service. Under Rule 36(a)(3), any matter not timely answered is deemed admitted.",
                _ => "These Requests for Production are served under Federal Rules of Civil Procedure 26 and 34. Serve your written response and produce the documents within thirty (30) days of service as required by Rule 34(b)(2)(A)."
            });
        }

        instructions.Add(kind switch
        {
            DiscoveryKind.Interrogatories => "If you object to part of an Interrogatory, state the objection with specificity and answer the remainder.",
            DiscoveryKind.RequestsForAdmission => "If you cannot admit or deny a request, state in detail why. A denial must fairly respond to the substance of the request, and you must specify the part admitted and qualify or deny the rest.",
            _ => "Produce documents as they are kept in the usual course of business, or organized and labeled to correspond to each request, and state whether any responsive documents are being withheld on the basis of an objection."
        });
        instructions.Add(state
            ? "Produce electronically stored information in native format with all metadata intact, including emails with their attachments and family relationships preserved."
            : "Produce electronically stored information in native format with all metadata intact, including emails with their attachments and family relationships preserved, as permitted by Rule 34(b)(1)(C).");
        instructions.Add(state
            ? "If you withhold any document or information on a claim of privilege or work product, provide a privilege log identifying its date, author, recipients, subject matter and the privilege claimed."
            : "If you withhold any document or information on a claim of privilege or work product, provide a privilege log as required by Rule 26(b)(5)(A) identifying its date, author, recipients, subject matter and the privilege claimed.");
        instructions.Add(state
            ? "These requests are continuing. You must supplement or amend your responses if you learn that a response is incomplete or incorrect."
            : "These requests are continuing. Under Rule 26(e), you must supplement or amend your responses if you learn that a response is incomplete or incorrect.");

        return instructions;
    }

    public static string RequestLabel(DiscoveryKind kind, DiscoveryVariant variant) => (variant, kind) switch
    {
        (DiscoveryVariant.MemoCriminal, _) => "ITEM NO.",
        (DiscoveryVariant.MemoCivil, DiscoveryKind.Interrogatories) => "QUESTION NO.",
        (DiscoveryVariant.MemoCivil, DiscoveryKind.RequestsForAdmission) => "REQUEST TO ADMIT NO.",
        (_, DiscoveryKind.Interrogatories) => "INTERROGATORY NO.",
        (_, DiscoveryKind.RequestsForAdmission) => "REQUEST FOR ADMISSION NO.",
        _ => "REQUEST FOR PRODUCTION NO."
    };

    /// <summary>
    /// Signature block in the same line shape as CoreDocumentAssembler.BuildSignatureBlock (first line dated, rest on the
    /// right half in WriteSignatureBlock). Names come only from the cast and the core document; a bar number is printed only
    /// when the core document's own counsel signs (T-08-22).
    /// </summary>
    public static string SignatureText(CaseDraft draft, CaseParty propounding, string serviceDate)
    {
        var doc = draft?.CoreDocument;
        var sb = new StringBuilder();
        sb.AppendLine($"Dated: {CaseGuideDocxExporter.LongDate(serviceDate)}");
        sb.AppendLine();

        var partyId = propounding?.Id;
        var plaintiffSide = propounding != null && CounselDirectory.IsPlaintiffSide(propounding.Role);
        var partner = CounselDirectory.Partner(draft!, partyId);
        var firm = CounselDirectory.FirmFor(draft!, partyId);

        // Memo variants: the government or company side signs as the memo author
        if (doc != null && DiscoveryVariants.IsMemo(DiscoveryVariants.Of(doc)) && (plaintiffSide || partner == null))
        {
            AppendIfAny(sb, doc.Memo?.From);
            AppendIfAny(sb, doc.Memo?.Organization);
            return sb.ToString();
        }

        sb.AppendLine("Respectfully submitted,");
        sb.AppendLine();

        if (partner != null && firm != null)
        {
            var counsel = doc?.Counsel;
            var isCoreCounsel = counsel != null && plaintiffSide
                && string.Equals(counsel.AttorneyName?.Trim(), partner.FullName, StringComparison.OrdinalIgnoreCase);

            sb.AppendLine(firm.Name);
            sb.AppendLine();
            sb.AppendLine($"By: /s/ {partner.FullName}");
            sb.AppendLine(partner.FullName);
            if (isCoreCounsel && !string.IsNullOrWhiteSpace(counsel!.BarNumber))
                sb.AppendLine($"Bar No. {counsel.BarNumber.Trim()}");
            var address = isCoreCounsel && !string.IsNullOrWhiteSpace(counsel!.Address) ? counsel.Address : firm.Location;
            AppendIfAny(sb, address);
            if (!string.IsNullOrWhiteSpace(partner.Phone))
                sb.AppendLine($"Tel: {partner.Phone.Trim()}");
            AppendIfAny(sb, partner.Email);
        }
        else if (plaintiffSide && doc?.Counsel != null && !string.IsNullOrWhiteSpace(doc.Counsel.AttorneyName))
        {
            // The core document's own counsel represents the original plaintiff side
            var counsel = doc.Counsel;
            AppendIfAny(sb, counsel.FirmName);
            sb.AppendLine();
            sb.AppendLine($"By: /s/ {counsel.AttorneyName.Trim()}");
            sb.AppendLine(counsel.AttorneyName.Trim());
            if (!string.IsNullOrWhiteSpace(counsel.BarNumber))
                sb.AppendLine($"Bar No. {counsel.BarNumber.Trim()}");
            AppendIfAny(sb, counsel.Address);
            if (!string.IsNullOrWhiteSpace(counsel.Phone))
                sb.AppendLine($"Tel: {counsel.Phone.Trim()}");
            AppendIfAny(sb, counsel.Email);
        }
        else
        {
            // No counsel in the guide: never invent a name
            sb.AppendLine("By: /s/ Counsel of record");
        }

        sb.AppendLine();
        sb.AppendLine($"Attorneys for {RoleTitle(doc, propounding)} {Name(propounding)}");
        return sb.ToString();
    }

    public static string CertificateText(CaseDraft draft, CaseParty responding, string serviceDate)
    {
        var date = CaseGuideDocxExporter.LongDate(serviceDate);
        var doc = draft?.CoreDocument;
        var to = $"{RoleTitle(doc, responding)} {Name(responding)}";
        var partner = CounselDirectory.Partner(draft!, responding?.Id);
        var firm = CounselDirectory.FirmFor(draft!, responding?.Id);

        if (partner != null && !string.IsNullOrWhiteSpace(partner.Email))
        {
            var firmName = firm != null && !string.IsNullOrWhiteSpace(firm.Name) ? $", {firm.Name.Trim()}" : string.Empty;
            return $"I hereby certify that on {date}, I served the foregoing on counsel of record for {to}, {partner.FullName}{firmName}, "
                + $"by electronic mail at {partner.Email.Trim()}.";
        }

        return $"I hereby certify that on {date}, I served the foregoing on counsel of record for {to}.";
    }

    private static List<string> MemoCaptionLines(CoreDocument doc, string number)
    {
        var organization = (doc.Memo?.Organization ?? string.Empty).Trim();
        if (organization.Length == 0)
            organization = (doc.Jurisdiction?.CourtName ?? string.Empty).Trim();

        var lines = new List<string>();
        if (organization.Length > 0)
        {
            lines.Add(organization.ToUpperInvariant());
            lines.Add(string.Empty);
        }
        lines.Add($"IN THE MATTER OF {(doc.Title ?? string.Empty).Trim().ToUpperInvariant()}");
        lines.Add(string.Empty);
        lines.Add($"{CaseNumber.Label(doc)} {number}");
        return lines;
    }

    private static bool IsState(CoreDocument doc) => doc?.Jurisdiction?.CourtSystem == CourtSystem.State;

    private static string StateName(CoreDocument doc)
    {
        var state = (doc?.Jurisdiction?.State ?? string.Empty).Trim();
        return state.Length > 0 ? state : "applicable";
    }

    private static string RelevantPeriod(CoreDocument? doc)
    {
        var start = CaseGuideDocxExporter.LongDate(doc?.TimelineStart ?? string.Empty);
        var end = CaseGuideDocxExporter.LongDate(doc?.TimelineEnd ?? string.Empty);
        if (start.Length > 0 && end.Length > 0)
            return $"{start} through {end}";
        if (start.Length > 0)
            return $"{start} through the date of your response";
        if (end.Length > 0)
            return $"the period ending {end}";
        return "the period covered by the events described in the pleadings";
    }

    // "Plaintiff Brightline Glazing, LLC"
    private static string PartyRef(CoreDocument doc, CaseParty party) => $"{RoleTitle(doc, party)} {Name(party)}";

    private static string Name(CaseParty? party) => (party?.Name ?? string.Empty).Trim();

    private static string RoleTitle(CoreDocument? doc, CaseParty? party)
    {
        var petition = doc?.UsesPetitionTerminology == true;
        var title = party?.Role switch
        {
            PartyRole.Plaintiff => petition ? "Petitioner" : "Plaintiff",
            PartyRole.Defendant => petition ? "Respondent" : "Defendant",
            PartyRole.Complainant => "Complainant",
            PartyRole.Subject => "Subject",
            _ => "Non-Party"
        };
        return English.TextInfo.ToTitleCase(title);
    }

    private static void AppendIfAny(StringBuilder sb, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
            sb.AppendLine(line.Trim());
    }

    private static List<string> SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}
