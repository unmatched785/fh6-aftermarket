using Fh6Aftermarket.Ocr;
using Fh6Aftermarket.Safety;

namespace Fh6Aftermarket.Gui;

public static class GuiLauncher
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var repoRoot = FindRepoRoot();
                var safety = SafetySettingsLoader.Load(
                    Path.Combine(repoRoot, "config", "safety.json"));
                var catalog = TargetCatalog.Load(
                    Path.Combine(repoRoot, "config", "targets.json"));
                var officialCars = OfficialCarCatalog.Load(
                    Path.Combine(repoRoot, "config", "official-cars.json"));
                var tesseract = TesseractInstallationLocator.Locate();
                var analyzer = new AftermarketMapCardAnalyzer(
                    new TesseractCliRecognizer(
                        tesseract.ExecutablePath,
                        tesseract.TessdataPath),
                    new TargetTextMatcher(
                        catalog,
                        new CarNameNormalizer(officialCars)));
                var languageDetector = new PauseMenuLanguageDetector(
                    new TesseractCliRecognizer(
                        tesseract.ExecutablePath,
                        tesseract.TessdataPath,
                        "eng+kor",
                        [11],
                        preparationScale: 1,
                        maximumPreparedWidth: 1600));
                var controller = new ValidationSessionController(
                    safety,
                    analyzer,
                    languageDetector,
                    Path.Combine(repoRoot, "logs"));

                Application.Run(new AftermarketMainForm(controller));
            }
            catch (Exception exception)
            {
                failure = exception;
                MessageBox.Show(
                    exception.Message,
                    "FH6 Aftermarket 시작 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("GUI failed to start.", failure);
        }
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "config", "targets.json")) &&
                    File.Exists(Path.Combine(directory.FullName, "config", "safety.json")) &&
                    File.Exists(Path.Combine(directory.FullName, "config", "official-cars.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "config/targets.json, config/safety.json, and config/official-cars.json " +
            "could not be located.");
    }
}
