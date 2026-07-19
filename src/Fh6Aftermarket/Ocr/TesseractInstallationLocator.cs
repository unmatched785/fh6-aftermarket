namespace Fh6Aftermarket.Ocr;

public sealed record TesseractInstallation(
    string ExecutablePath,
    string TessdataPath);

public sealed record TesseractSearchContext(
    string AppBaseDirectory,
    string? UserProfile = null,
    string? ScoopRoot = null,
    string? ProgramFiles = null,
    string? ProgramFilesX86 = null,
    string? PathValue = null,
    string? ExecutableOverride = null,
    string? TessdataOverride = null,
    string? TessdataPrefix = null)
{
    public static TesseractSearchContext FromCurrentProcess()
        => new(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("SCOOP"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("FH6_TESSERACT_EXE"),
            Environment.GetEnvironmentVariable("FH6_TESSDATA_DIR"),
            Environment.GetEnvironmentVariable("TESSDATA_PREFIX"));
}

public static class TesseractInstallationLocator
{
    public static TesseractInstallation Locate()
        => Locate(TesseractSearchContext.FromCurrentProcess());

    public static TesseractInstallation Locate(TesseractSearchContext context)
    {
        var executableCandidates = CreateExecutableCandidates(context);
        var tessdataCandidates = CreateTessdataCandidates(context, executableCandidates);

        var executablePath = FindExecutable(executableCandidates);
        var tessdataPath = FindTessdata(tessdataCandidates);

        if (executablePath is null || tessdataPath is null)
        {
            throw new FileNotFoundException(BuildFailureMessage(
                executablePath,
                tessdataPath,
                executableCandidates,
                tessdataCandidates));
        }

        return new TesseractInstallation(executablePath, tessdataPath);
    }

    private static IReadOnlyList<string> CreateExecutableCandidates(TesseractSearchContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.ExecutableOverride))
        {
            return [context.ExecutableOverride];
        }

        var candidates = new List<string>();
        Add(candidates, context.AppBaseDirectory, "tesseract.exe");
        Add(candidates, context.AppBaseDirectory, "tesseract", "tesseract.exe");

        foreach (var scoopRoot in ScoopRoots(context))
        {
            Add(candidates, scoopRoot, "apps", "tesseract", "current", "tesseract.exe");
        }

        Add(candidates, context.ProgramFiles, "Tesseract-OCR", "tesseract.exe");
        Add(candidates, context.ProgramFilesX86, "Tesseract-OCR", "tesseract.exe");

        foreach (var directory in SplitPath(context.PathValue))
        {
            Add(candidates, directory, "tesseract.exe");
        }

        return Distinct(candidates);
    }

    private static IReadOnlyList<string> CreateTessdataCandidates(
        TesseractSearchContext context,
        IReadOnlyList<string> executableCandidates)
    {
        if (!string.IsNullOrWhiteSpace(context.TessdataOverride))
        {
            return [context.TessdataOverride];
        }

        var candidates = new List<string>();
        Add(candidates, context.AppBaseDirectory, "tessdata");
        Add(candidates, context.AppBaseDirectory, "tesseract", "tessdata");

        foreach (var executable in executableCandidates)
        {
            var directory = Path.GetDirectoryName(executable);
            Add(candidates, directory, "tessdata");
        }

        if (!string.IsNullOrWhiteSpace(context.TessdataPrefix))
        {
            candidates.Add(context.TessdataPrefix);
            Add(candidates, context.TessdataPrefix, "tessdata");
        }

        foreach (var scoopRoot in ScoopRoots(context))
        {
            Add(candidates, scoopRoot, "apps", "tesseract-languages", "current");
            Add(candidates, scoopRoot, "apps", "tesseract-languages", "current", "tessdata");
            Add(candidates, scoopRoot, "apps", "tesseract", "current", "tessdata");
        }

        Add(candidates, context.ProgramFiles, "Tesseract-OCR", "tessdata");
        Add(candidates, context.ProgramFilesX86, "Tesseract-OCR", "tessdata");
        return Distinct(candidates);
    }

    private static IEnumerable<string> ScoopRoots(TesseractSearchContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.ScoopRoot))
        {
            yield return context.ScoopRoot;
        }

        if (!string.IsNullOrWhiteSpace(context.UserProfile))
        {
            yield return Path.Combine(context.UserProfile, "scoop");
        }
    }

    private static IEnumerable<string> SplitPath(string? pathValue)
        => (pathValue ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.Trim('"'))
            .Where(value => value.Length > 0);

    private static string? FindExecutable(IEnumerable<string> candidates)
        => candidates
            .Select(Clean)
            .FirstOrDefault(File.Exists);

    private static string? FindTessdata(IEnumerable<string> candidates)
        => candidates
            .Select(Clean)
            .FirstOrDefault(ContainsRequiredLanguages);

    private static bool ContainsRequiredLanguages(string path)
        => Directory.Exists(path) &&
           File.Exists(Path.Combine(path, "eng.traineddata")) &&
           File.Exists(Path.Combine(path, "kor.traineddata"));

    private static string Clean(string path)
    {
        var value = path.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(value);
        }
        catch
        {
            return value;
        }
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(Clean)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void Add(List<string> candidates, params string?[] parts)
    {
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            return;
        }

        candidates.Add(Path.Combine(parts!));
    }

    private static string BuildFailureMessage(
        string? executablePath,
        string? tessdataPath,
        IReadOnlyList<string> executableCandidates,
        IReadOnlyList<string> tessdataCandidates)
    {
        var missing = new List<string>();
        if (executablePath is null)
        {
            missing.Add("tesseract.exe");
        }

        if (tessdataPath is null)
        {
            missing.Add("eng.traineddata / kor.traineddata");
        }

        return
            $"Tesseract 구성요소를 자동으로 찾지 못했습니다: {string.Join(", ", missing)}.\n\n" +
            "표준 설치 폴더, Scoop 실제 루트, PATH, 앱 폴더를 모두 확인했습니다.\n" +
            "직접 지정하려면 환경 변수 FH6_TESSERACT_EXE와 FH6_TESSDATA_DIR을 설정하세요.\n\n" +
            $"확인한 실행 파일 경로:\n- {string.Join("\n- ", executableCandidates)}\n\n" +
            $"확인한 언어 데이터 경로:\n- {string.Join("\n- ", tessdataCandidates)}";
    }
}
