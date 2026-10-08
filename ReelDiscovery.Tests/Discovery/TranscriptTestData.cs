using ReelDiscovery.Models.Discovery;

namespace ReelDiscovery.Tests.Discovery;

/// <summary>
/// Deposition content built without a model call for the transcript layout and .docx tests: five speakers (examining
/// MS Quist, defending MR Hale, witness Michael Scott, reporter, videographer), exhibits 1-3 introduced at turns 10, 50
/// and 90 (when present), a long answer at turn 5, an objection at turn 20 and a parenthetical at turn 30.
/// </summary>
internal static class TranscriptTestData
{
    public const string LongAnswer =
        "Well, the window units arrived on site in late March, and when we opened the first pallets we saw that the " +
        "frames were the older thermal-break design rather than the upgraded assembly listed in the approved " +
        "submittal, so I called the project manager right away to ask whether anyone had signed off on a substitution.";

    public static DepositionContent Sample(int turnCount = 120, bool memo = false)
    {
        var content = new DepositionContent
        {
            WitnessId = "CH-013",
            WitnessName = "Michael Scott",
            DocumentTitle = memo ? "Examination of Michael Scott (Excerpt)" : "Deposition of Michael Scott (Excerpt)",
            CaptionLines = new List<string>
            {
                "UNITED STATES DISTRICT COURT",
                "NORTHERN DISTRICT OF CALIFORNIA",
                "BRIGHTLINE DEVELOPMENT LLC, Plaintiff,",
                "v.",
                "HARMON GLAZING INC., Defendant."
            },
            CaseNumber = "3:25-cv-04821",
            Date = "2025-09-16",
            StartTime = "9:30 a.m.",
            Location = "Hale & Ward LLP, 100 Market Street, Suite 2400, San Francisco, California",
            ReporterName = "Janet Moore",
            ReporterCsr = "CSR No. 13021",
            VideographerName = "Carl Brooks",
            IsMemo = memo,
            TargetTestimonyPages = 25,
            TopicTitles = new List<string> { "Background", "Window submittals", "Substitution" },
            Speakers = new List<DepositionSpeaker>
            {
                new()
                {
                    SpeakerId = "CH-011", FullName = "Ingrid Quist", Role = SpeakerRole.Examining, Honorific = "MS",
                    FirmName = "Quist Partners LLP", Email = "iquist@quistpartners.com", PartyLabel = "For Plaintiff Brightline Development LLC"
                },
                new()
                {
                    SpeakerId = "CH-012", FullName = "Daniel Hale", Role = SpeakerRole.Defending, Honorific = "MR",
                    FirmName = "Hale & Ward LLP", Email = "dhale@haleward.com", PartyLabel = "For Defendant Harmon Glazing Inc."
                },
                new() { SpeakerId = "CH-013", FullName = "Michael Scott", Role = SpeakerRole.Witness },
                new() { SpeakerId = "REP", FullName = "Janet Moore", Role = SpeakerRole.Reporter },
                new() { SpeakerId = "VID", FullName = "Carl Brooks", Role = SpeakerRole.Videographer }
            },
            Exhibits = new List<DepositionExhibit>
            {
                new() { Number = 1, Description = "Email from R. Harmon to L. Chen re window submittal", DocId = "BRL-000101", Date = "2024-03-04" },
                new() { Number = 2, Description = "Approved window submittal package, revision 2", DocId = "BRL-000245", Date = "2024-02-12" },
                new() { Number = 3, Description = "Field report documenting water intrusion at level 4 east elevation", DocId = "BRL-000377", Date = "2024-11-20" }
            }
        };

        for (var i = 0; i < turnCount; i++)
        {
            var turn = i % 2 == 0
                ? new DepositionTurn
                {
                    Kind = TurnKind.Q,
                    SpeakerId = "CH-011",
                    Text = $"Question {i}: what did you understand about the window schedule at that time" +
                           (i % 3 == 0 ? " and who else on the project team was aware of it?" : "?")
                }
                : new DepositionTurn
                {
                    Kind = TurnKind.A,
                    SpeakerId = "CH-013",
                    Text = i % 5 == 0 ? "No." : $"Answer {i}: I believe that was discussed at the weekly meeting."
                };

            if (i == 5)
                turn.Text = LongAnswer;
            if (i == 20)
                turn = new DepositionTurn { Kind = TurnKind.Objection, SpeakerId = "CH-012", Text = "Objection, form." };
            if (i == 30)
                turn = new DepositionTurn { Kind = TurnKind.Parenthetical, Text = "Discussion off the record." };
            if (i is 10 or 50 or 90)
            {
                var n = i == 10 ? 1 : i == 50 ? 2 : 3;
                turn = new DepositionTurn
                {
                    Kind = TurnKind.Q,
                    SpeakerId = "CH-011",
                    Text = $"I am handing you what has been marked as Exhibit {n}. Do you recognize this document?",
                    ExhibitNumber = n
                };
            }

            content.Turns.Add(turn);
        }

        return content;
    }
}
