using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

public class AnswerKeyRelabelStore
{
    public const string FileName = "answer-key-relabels.json";
    public const long MaxFileBytes = 64L * 1024 * 1024;

    private readonly string _caseFolder;

    public AnswerKeyRelabelStore(string caseFolder)
    {
        _caseFolder = caseFolder ?? string.Empty;
    }

    public string FilePath => Path.Combine(_caseFolder, FileName);

    public AnswerKeyRelabelSet Load() => throw new NotImplementedException();

    public void Save(AnswerKeyRelabelSet set) => throw new NotImplementedException();
}
