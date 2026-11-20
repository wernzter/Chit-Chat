using ChitChat.Speech;
using Xunit;

namespace ChitChat.App.Tests;

public sealed class ZipformerTests
{
    [Fact]
    public void Streaming_requires_its_own_complete_model_bundle()
    {
        var folder = Directory.CreateTempSubdirectory("chitchat-zipformer-test-");
        try
        {
            WriteSizedFile(ZipformerTranscriber.Encoder, 71_083_163);
            WriteSizedFile(ZipformerTranscriber.Decoder, 1_307_236);
            WriteSizedFile(ZipformerTranscriber.Joiner, 259_335);
            WriteSizedFile(ZipformerTranscriber.Vocabulary, 5_048);
            WriteSizedFile("LICENSE.txt", 11_689);
            Assert.False(ZipformerTranscriber.IsComplete(folder.FullName));

            WriteSizedFile(ZipformerTranscriber.Bpe, 12_590);
            Assert.True(ZipformerTranscriber.IsComplete(folder.FullName));
            Assert.False(ParakeetTranscriber.IsComplete(folder.FullName));
            Assert.Equal(72_654_782, SpeechModelInstaller.DownloadSize(SpeechModelVariant.StreamingEnglish));
        }
        finally
        {
            folder.Delete(recursive: true);
        }

        void WriteSizedFile(string name, long bytes)
        {
            using var stream = File.Create(Path.Combine(folder.FullName, name));
            stream.SetLength(bytes);
        }
    }

    [Fact]
    public void Streaming_hotwords_are_bounded_and_exclude_native_file_syntax()
    {
        string[] hints = [" New York ", "new york", "Line\nBreak", "custom:1000", "Café", "Route 42", "Dash-name", new string('a', 65)];
        var candidates = Enumerable.Range(0, 90).Select(i => $"Hint {(char)('A' + i / 26)}{(char)('A' + i % 26)}");
        var words = ZipformerTranscriber.PrepareHotwords(hints.Concat(candidates));
        var lines = words.Split('\n');
        Assert.Equal("NEW YORK", lines[0]);
        Assert.Equal("HINT AA", lines[1]);
        Assert.Equal(64, lines.Length);
        Assert.Equal("HINT CK", lines[^1]);
    }
}
