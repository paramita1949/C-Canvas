using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ImageColorChanger.Services;
using Xunit;
using Xunit.Abstractions;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleSpeechReferenceParserEvaluationTests
    {
        private readonly ITestOutputHelper _output;

        public BibleSpeechReferenceParserEvaluationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void GoldenSet_ShouldMeetThresholds()
        {
            GoldenSetFixture fixture = LoadFixture();
            Assert.NotNull(fixture);
            Assert.NotNull(fixture.Samples);
            Assert.NotEmpty(fixture.Samples);

            int tp = 0;
            int fp = 0;
            int tn = 0;
            int fn = 0;
            int wrongBook = 0;
            int abstainCount = 0;

            int fuzzyExpected = 0;
            int fuzzyHit = 0;

            foreach (var sample in fixture.Samples)
            {
                bool ok = BibleSpeechReferenceParser.TryParse(sample.Text, out var parsed);
                if (!ok)
                {
                    abstainCount++;
                }

                bool expectAccept = string.Equals(sample.Expect, "accept", StringComparison.OrdinalIgnoreCase);
                bool exactMatch = ok
                                  && parsed.BookId == sample.BookId
                                  && parsed.Chapter == sample.Chapter
                                  && parsed.StartVerse == sample.StartVerse
                                  && parsed.EndVerse == sample.EndVerse;

                if (expectAccept)
                {
                    if (sample.Bucket != null && sample.Bucket.StartsWith("positives_fuzzy", StringComparison.Ordinal))
                    {
                        fuzzyExpected++;
                        if (exactMatch)
                        {
                            fuzzyHit++;
                        }
                    }

                    if (exactMatch)
                    {
                        tp++;
                    }
                    else
                    {
                        fn++;
                        if (ok)
                        {
                            fp++;
                            wrongBook++;
                        }
                    }
                }
                else
                {
                    if (ok)
                    {
                        fp++;
                    }
                    else
                    {
                        tn++;
                    }
                }
            }

            double precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
            double recall = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            double fpr = fp + tn == 0 ? 0 : (double)fp / (fp + tn);
            double abstainRate = fixture.Samples.Count == 0 ? 0 : (double)abstainCount / fixture.Samples.Count;
            double fuzzyRecall = fuzzyExpected == 0 ? 0 : (double)fuzzyHit / fuzzyExpected;
            double wrongBookRate = tp + wrongBook == 0 ? 0 : (double)wrongBook / (tp + wrongBook);

            _output.WriteLine($"sampleCount={fixture.Samples.Count}, tp={tp}, fp={fp}, tn={tn}, fn={fn}, wrongBook={wrongBook}");
            _output.WriteLine($"precision={precision:F6}, recall={recall:F6}, fpr={fpr:F6}, abstainRate={abstainRate:F6}, fuzzyRecall={fuzzyRecall:F6}, wrongBookRate={wrongBookRate:F6}");
            Console.WriteLine($"sampleCount={fixture.Samples.Count}, tp={tp}, fp={fp}, tn={tn}, fn={fn}, wrongBook={wrongBook}");
            Console.WriteLine($"precision={precision:F6}, recall={recall:F6}, fpr={fpr:F6}, abstainRate={abstainRate:F6}, fuzzyRecall={fuzzyRecall:F6}, wrongBookRate={wrongBookRate:F6}");

            Assert.True(precision >= 0.99, $"precision too low: {precision:F6}");
            Assert.True(wrongBookRate <= 0.003, $"wrongBookRate too high: {wrongBookRate:F6}");
            Assert.True(fuzzyRecall >= 0.95, $"fuzzyRecall too low: {fuzzyRecall:F6}");
        }

        private static GoldenSetFixture LoadFixture()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Services", "Fixtures", "bible_speech_parser_golden_set.json");
            Assert.True(File.Exists(path), $"Fixture not found: {path}");

            string json = File.ReadAllText(path);
            var fixture = JsonSerializer.Deserialize<GoldenSetFixture>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(fixture);
            return fixture;
        }

        private sealed class GoldenSetFixture
        {
            public string Version { get; set; } = string.Empty;
            public string GeneratedAt { get; set; } = string.Empty;
            public int SampleCount { get; set; }
            public List<GoldenSetSample> Samples { get; set; } = new List<GoldenSetSample>();
        }

        private sealed class GoldenSetSample
        {
            public string Text { get; set; } = string.Empty;
            public string Expect { get; set; } = string.Empty;
            public int BookId { get; set; }
            public int Chapter { get; set; }
            public int StartVerse { get; set; }
            public int EndVerse { get; set; }
            public string Bucket { get; set; } = string.Empty;
        }
    }
}
