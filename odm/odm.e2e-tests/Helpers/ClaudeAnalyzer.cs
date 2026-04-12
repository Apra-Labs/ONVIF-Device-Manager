using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace odm.e2e_tests.Helpers
{
    /// <summary>
    /// Sends screenshots to Claude via the claude CLI (fleet OAuth — no API key required).
    /// Invocation: claude --print --image &lt;path&gt; then prompt via stdin.
    /// Adjust CLI flags if the fleet claude CLI version changes.
    /// </summary>
    public class ClaudeAnalyzer
    {
        private readonly string _model;

        public ClaudeAnalyzer(string model = "claude-sonnet-4-6")
        {
            _model = model;
        }

        public AnalysisResult AnalyzeScreenshot(
            string screenshotPath,
            string scenarioContext,
            string[] expectedElements,
            string[] errorIndicators)
        {
            var prompt = BuildPrompt(scenarioContext, expectedElements, errorIndicators);

            // Shell out to claude CLI using fleet OAuth
            // --print: non-interactive, prints response to stdout
            // --image: passes the screenshot file to the model
            var psi = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = $"--print --image \"{screenshotPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8
            };

            string output;
            string stderr;
            int exitCode;

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                process.StandardInput.WriteLine(prompt);
                process.StandardInput.Close();

                output = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(60000);
                exitCode = process.ExitCode;
            }

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return new AnalysisResult
                {
                    Pass = false,
                    Confidence = 0.0,
                    Summary = $"Claude CLI failed (exit {exitCode}): {stderr?.Trim()}",
                    ErrorsDetected = new List<string> { "Claude analysis unavailable" }
                };
            }

            return ParseResponse(output);
        }

        private static string BuildPrompt(string scenarioContext, string[] expectedElements, string[] errorIndicators)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are a QA analyst reviewing a screenshot of the ONVIF Device Manager (ODM) desktop application.");
            sb.AppendLine();
            sb.AppendLine($"**Scenario:** {scenarioContext}");
            sb.AppendLine();
            sb.AppendLine("**Expected elements on screen:**");
            foreach (var el in expectedElements ?? new string[0])
                sb.AppendLine($"- {el}");
            sb.AppendLine();
            sb.AppendLine("**Error indicators to check for:**");
            foreach (var err in errorIndicators ?? new string[0])
                sb.AppendLine($"- {err}");
            sb.AppendLine();
            sb.AppendLine("Analyze the screenshot and respond with EXACTLY this JSON structure (no markdown, raw JSON only):");
            sb.AppendLine("{");
            sb.AppendLine("  \"pass\": true/false,");
            sb.AppendLine("  \"confidence\": 0.0-1.0,");
            sb.AppendLine("  \"summary\": \"one-line summary of what you see\",");
            sb.AppendLine("  \"expected_found\": [\"list of expected elements that ARE visible\"],");
            sb.AppendLine("  \"expected_missing\": [\"list of expected elements that are NOT visible\"],");
            sb.AppendLine("  \"errors_detected\": [\"list of any error indicators found\"],");
            sb.AppendLine("  \"notes\": \"any additional observations\"");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static AnalysisResult ParseResponse(string output)
        {
            // Strip any markdown fences if present
            var json = output.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start >= 0 && end > start)
                    json = json.Substring(start, end - start + 1);
            }

            try
            {
                return JsonConvert.DeserializeObject<AnalysisResult>(json)
                    ?? new AnalysisResult { Pass = false, Confidence = 0, Summary = "Empty response from Claude" };
            }
            catch (Exception ex)
            {
                return new AnalysisResult
                {
                    Pass = false,
                    Confidence = 0.0,
                    Summary = $"Failed to parse Claude response: {ex.Message}",
                    Notes = output
                };
            }
        }

        public static bool DeterminePassFail(AnalysisResult result)
        {
            if (!result.Pass) return false;
            if (result.Confidence < 0.7) return false;
            if (result.ExpectedMissing != null && result.ExpectedMissing.Count > 0) return false;
            if (result.ErrorsDetected != null && result.ErrorsDetected.Count > 0) return false;
            return true;
        }
    }

    public class AnalysisResult
    {
        [JsonProperty("pass")]
        public bool Pass { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("expected_found")]
        public List<string> ExpectedFound { get; set; } = new List<string>();

        [JsonProperty("expected_missing")]
        public List<string> ExpectedMissing { get; set; } = new List<string>();

        [JsonProperty("errors_detected")]
        public List<string> ErrorsDetected { get; set; } = new List<string>();

        [JsonProperty("notes")]
        public string Notes { get; set; }
    }
}
