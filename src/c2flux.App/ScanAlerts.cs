using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace c2flux
{
    public static class ScanAlerts
    {
        // Port of StatusMainFormController.ReportSkippedDirectories: an
        // information for the expected "System Volume Information" access
        // denial, a warning for every other folder the scan could not read.
        public static void ReportSkippedDirectories(int skippedDirectories, IReadOnlyList<string> details)
        {
            if (skippedDirectories <= 0)
            {
                return;
            }

            List<string> expected = new List<string>();
            List<string> warnings = new List<string>();

            foreach (string detail in details)
            {
                string path = detail.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[0]
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                bool isExpected =
                    path.EndsWith(Path.DirectorySeparatorChar + "System Volume Information", StringComparison.OrdinalIgnoreCase) &&
                    new[] { "0xC0000022", "Zugriff verweigert", "Access is denied", "Access denied" }
                        .Any(text => detail.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);

                (isExpected ? expected : warnings).Add(detail);
            }

            int unknown = Math.Max(0, skippedDirectories - details.Count);

            if (unknown > 0)
            {
                warnings.Add(LocalizationService.Format("Alert.UnknownSkippedDirectories", unknown));
            }

            string separator = Environment.NewLine + Environment.NewLine;

            if (expected.Count > 0)
            {
                AppAlertLog.AddInformation(
                    LocalizationService.GetText("Alert.Scan"),
                    expected.Count == 1
                        ? LocalizationService.GetText("Alert.ExpectedSystemDirectorySingle")
                        : LocalizationService.Format("Alert.ExpectedSystemDirectoryMultiple", expected.Count),
                    string.Join(separator, expected));
            }

            if (warnings.Count > 0)
            {
                AppAlertLog.AddWarning(
                    LocalizationService.GetText("Alert.Scan"),
                    warnings.Count == 1
                        ? LocalizationService.GetText("Alert.SkippedDirectorySingle")
                        : LocalizationService.Format("Alert.SkippedDirectoryMultiple", warnings.Count),
                    string.Join(separator, warnings));
            }
        }
    }
}
