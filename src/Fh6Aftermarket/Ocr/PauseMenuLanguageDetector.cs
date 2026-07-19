using System.Drawing;
using Fh6Aftermarket.Domain;

namespace Fh6Aftermarket.Ocr;

public sealed record PauseMenuLanguageDetection(
    GameLanguage? Language,
    string RecognizedText,
    int EnglishScore,
    int KoreanScore);

public sealed class PauseMenuLanguageDetector
{
    private sealed record LanguageSignal(string Text, int Weight);

    private static readonly LanguageSignal[] EnglishSignals =
    [
        new("WORLDMAP", 3),
        new("CAMPAIGN", 3),
        new("CARS", 2),
        new("MYHORIZON", 3),
        new("ONLINE", 2),
        new("CREATIVEHUB", 3),
        new("COLLECTIONJOURNAL", 3),
        new("WHATSNEXT", 3),
        new("SETTINGS", 3),
        new("EXITGAME", 3),
        new("RESETCARPOSITION", 2),
        new("CONVOY", 2),
        new("SERIESUPDATE", 2),
        new("SELECT", 1),
        new("BACK", 1)
    ];

    private static readonly LanguageSignal[] KoreanSignals =
    [
        new("세계지도", 3),
        new("세계", 2),
        new("지도", 2),
        new("캠페인", 3),
        new("자동차", 2),
        new("나의HORIZON", 3),
        new("온라인", 2),
        new("창작허브", 3),
        new("스토어", 2),
        new("컬렉션일지", 3),
        new("다음에할일", 3),
        new("설정", 3),
        new("게임나가기", 3),
        new("자동차위치초기화", 2),
        new("대열", 2),
        new("시리즈업데이트", 2),
        new("선택", 1),
        new("뒤로", 1)
    ];

    private readonly TesseractCliRecognizer _recognizer;

    public PauseMenuLanguageDetector(TesseractCliRecognizer recognizer)
    {
        _recognizer = recognizer;
    }

    public PauseMenuLanguageDetection Detect(Bitmap source)
    {
        if (!ScreenGeometry.TryCreate(source.Width, source.Height, out var geometry) ||
            geometry is null)
        {
            return new PauseMenuLanguageDetection(null, string.Empty, 0, 0);
        }

        // The language-specific pause-menu labels span the center of the screen.
        // Exclude the car/player header and keep both the left and right tiles.
        var menu = geometry.Scale(new PixelRect(180, 160, 1560, 700));
        var recognition = _recognizer.Recognize(
            source,
            new Rectangle(menu.X, menu.Y, menu.Width, menu.Height));
        return Classify(recognition.CombinedText);
    }

    public static PauseMenuLanguageDetection Classify(string recognizedText)
    {
        var normalized = TargetTextMatcher.Normalize(recognizedText);
        var englishScore = Score(normalized, EnglishSignals);
        var koreanScore = Score(normalized, KoreanSignals);

        GameLanguage? language = null;
        if (englishScore > koreanScore)
        {
            language = GameLanguage.English;
        }
        else if (koreanScore > englishScore)
        {
            language = GameLanguage.Korean;
        }
        else if (!string.IsNullOrWhiteSpace(recognizedText))
        {
            // ENG/KOR are the only supported states. If exact menu signals tie,
            // use the dominant script instead of failing because one label was
            // missed or split by OCR.
            var hangulCount = recognizedText.Count(IsHangulSyllable);
            var latinCount = recognizedText.Count(IsLatinLetter);
            language = hangulCount * 2 >= latinCount
                ? GameLanguage.Korean
                : GameLanguage.English;
        }

        return new PauseMenuLanguageDetection(
            language,
            recognizedText.Trim(),
            englishScore,
            koreanScore);
    }

    private static int Score(string normalized, IEnumerable<LanguageSignal> signals)
    {
        return signals.Sum(signal =>
            normalized.Contains(
                TargetTextMatcher.Normalize(signal.Text),
                StringComparison.Ordinal)
                ? signal.Weight
                : 0);
    }

    private static bool IsHangulSyllable(char character) =>
        character is >= '\uAC00' and <= '\uD7A3';

    private static bool IsLatinLetter(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
}
