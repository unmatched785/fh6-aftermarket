using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace Fh6Aftermarket.Ocr;

public sealed record OcrAttempt(
    int PageSegmentationMode,
    string Text,
    double BestWordConfidence);

public sealed record OcrRecognition(IReadOnlyList<OcrAttempt> Attempts)
{
    private const double MinimumReadableWordConfidence = 70;

    public string CombinedText => string.Join(
        " ",
        Attempts.Select(attempt => attempt.Text).Where(text => !string.IsNullOrWhiteSpace(text)));

    public OcrAttempt? PreferredVehicleNameAttempt => Attempts
        .Where(IsReadable)
        .OrderBy(attempt => attempt.PageSegmentationMode switch
        {
            7 => 0,
            13 => 1,
            11 => 2,
            _ => 3
        })
        .ThenByDescending(attempt => attempt.BestWordConfidence)
        .FirstOrDefault();

    public bool HasReadableText => PreferredVehicleNameAttempt is not null;

    private static bool IsReadable(OcrAttempt attempt)
    {
        var normalized = TargetTextMatcher.Normalize(attempt.Text);
        return attempt.BestWordConfidence >= MinimumReadableWordConfidence &&
               normalized.Length >= 4 &&
               normalized.Count(char.IsLetter) >= 3;
    }
}

public sealed class TesseractCliRecognizer
{
    private static readonly int[] PageSegmentationModes = [13, 7, 11];
    private readonly string _executablePath;
    private readonly string _tessdataPath;

    public TesseractCliRecognizer(string executablePath, string tessdataPath)
    {
        _executablePath = executablePath;
        _tessdataPath = tessdataPath;

        if (!Directory.Exists(_tessdataPath))
        {
            throw new DirectoryNotFoundException($"Tesseract language data was not found: {_tessdataPath}");
        }

        if (!File.Exists(Path.Combine(_tessdataPath, "eng.traineddata")))
        {
            throw new FileNotFoundException(
                "English Tesseract language data was not found.",
                Path.Combine(_tessdataPath, "eng.traineddata"));
        }
    }

    public OcrRecognition Recognize(Bitmap source, Rectangle region)
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fh6-aftermarket-ocr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var imagePath = Path.Combine(temporaryDirectory, "vehicle-name.png");
            using (var prepared = Prepare(source, region))
            {
                prepared.Save(imagePath, ImageFormat.Png);
            }

            var attempts = PageSegmentationModes
                .Select(mode => Run(imagePath, mode))
                .ToArray();
            return new OcrRecognition(attempts);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private OcrAttempt Run(string imagePath, int pageSegmentationMode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("--tessdata-dir");
        startInfo.ArgumentList.Add(_tessdataPath);
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add("eng");
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add(pageSegmentationMode.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("tessedit_create_tsv=1");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Tesseract.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Tesseract failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        return ParseTsv(output, pageSegmentationMode);
    }

    private static OcrAttempt ParseTsv(string tsv, int pageSegmentationMode)
    {
        var words = new List<string>();
        var bestConfidence = -1d;

        foreach (var line in tsv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var fields = line.Split('\t');
            if (fields.Length < 12 || fields[0] != "5")
            {
                continue;
            }

            var text = string.Join('\t', fields.Skip(11)).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            words.Add(text);
            if (double.TryParse(fields[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
            {
                bestConfidence = Math.Max(bestConfidence, confidence);
            }
        }

        return new OcrAttempt(pageSegmentationMode, string.Join(' ', words), bestConfidence);
    }

    private static Bitmap Prepare(Bitmap source, Rectangle region)
    {
        var safeRegion = Rectangle.Intersect(
            new Rectangle(0, 0, source.Width, source.Height),
            region);
        if (safeRegion.Width <= 0 || safeRegion.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "OCR region is outside the source image.");
        }

        const int scale = 4;
        var prepared = new Bitmap(
            safeRegion.Width * scale,
            safeRegion.Height * scale,
            PixelFormat.Format24bppRgb);

        using var graphics = Graphics.FromImage(prepared);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, prepared.Width, prepared.Height),
            safeRegion,
            GraphicsUnit.Pixel);
        return prepared;
    }
}
