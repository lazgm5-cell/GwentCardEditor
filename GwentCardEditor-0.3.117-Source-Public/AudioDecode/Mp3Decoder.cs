using System.Security.Cryptography;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GwentAudioDecode;

internal sealed class DecodeException(string code) : Exception(code);
internal sealed record DecodedAudio(int SampleRate, int Channels, double Seconds, string Sha256);

internal static class Mp3Decoder
{
    internal const int Rate = 48000, Channels = 2, BlockAlign = 4;
    internal const long MaxBytes = 120L * Rate * BlockAlign;
    internal static double Probe(string input)
    {
        using var source = OpenSource(input);
        using var reader = new Mp3FileReaderBase(source, format => new AcmMp3FrameDecompressor(format));
        double seconds = reader.TotalTime.TotalSeconds;
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 1800) throw new DecodeException("CustomMp3Limit");
        if (reader.WaveFormat.Channels is not (1 or 2)) throw new DecodeException("CustomMp3Failed");
        return seconds;
    }

    private static FileStream OpenSource(string input)
    {
        if (!Path.GetExtension(input).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) throw new DecodeException("CustomMp3Failed");
        var source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length > 50L * 1024 * 1024 || source.Length == 0)
        {
            bool empty = source.Length == 0; source.Dispose();
            throw new DecodeException(empty ? "CustomMp3Failed" : "CustomMp3Limit");
        }
        return source;
    }

    internal static DecodedAudio Decode(string input, string output, double? start = null, double? end = null)
    {
        input = Path.GetFullPath(input); output = Path.GetFullPath(output);
        string staged = output + ".mp3-tmp";
        if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || input.Equals(staged, StringComparison.OrdinalIgnoreCase)
            || File.Exists(output) || Directory.Exists(output)) throw new DecodeException("CustomMp3Output");
        using var source = OpenSource(input);
        using var reader = new Mp3FileReaderBase(source, format => new AcmMp3FrameDecompressor(format));
        if (reader.WaveFormat.Channels is not (1 or 2)) throw new DecodeException("CustomMp3Failed");
        bool segment = start.HasValue || end.HasValue;
        if (segment && (!start.HasValue || !end.HasValue || !double.IsFinite(start.Value) || !double.IsFinite(end.Value)
            || start.Value < 0 || end.Value <= start.Value || end.Value - start.Value > 120 + 1e-9
            || end.Value > reader.TotalTime.TotalSeconds + 1e-6 || reader.TotalTime.TotalSeconds > 1800)) throw new DecodeException("CustomAudioRange");
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.SampleRate != Rate) samples = new WdlResamplingSampleProvider(samples, Rate);
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        var pcm = samples.ToWaveProvider16();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        bool ownsStage = false;
        long written = 0;
        try
        {
            using (var file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsStage = true;
                using var writer = new WaveFileWriter(file, new WaveFormat(Rate, 16, Channels));
                if (segment) WriteSegment(pcm, writer, start!.Value, end!.Value, out written);
                else WriteBounded(pcm, writer, MaxBytes, out written);
            }
            if (written == 0) throw new DecodeException("CustomMp3Failed");
            string hash;
            using (var finished = File.OpenRead(staged)) hash = Convert.ToHexString(SHA256.HashData(finished)).ToLowerInvariant();
            File.Move(staged, output);
            return new(Rate, Channels, (double)written / (Rate * BlockAlign), hash);
        }
        finally { if (ownsStage) File.Delete(staged); }
    }

    internal static void WriteSegment(IWaveProvider input, Stream output, double start, double end, out long written)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end > 1800 || end - start > 120 + 1e-9)
            throw new DecodeException("CustomAudioRange");
        long skip = checked((long)Math.Round(start * Rate)) * BlockAlign;
        long wanted = checked((long)Math.Round(end * Rate)) * BlockAlign - skip;
        if (wanted <= 0 || wanted > MaxBytes) throw new DecodeException("CustomAudioRange");
        byte[] buffer = new byte[16384]; written = 0;
        // Sequential decoding preserves PCM sample positions across MP3 frames and resampling.
        while (skip > 0 || written < wanted)
        {
            int requested = (int)Math.Min(buffer.Length, skip > 0 ? skip : wanted - written);
            int count = input.Read(buffer, 0, requested);
            if (count <= 0 || count > requested || count % BlockAlign != 0) throw new DecodeException("CustomAudioRange");
            if (skip > 0) skip -= count;
            else { output.Write(buffer, 0, count); written += count; }
        }
    }

    internal static void WriteBounded(IWaveProvider input, Stream output, long maximum, out long written)
    {
        var buffer = new byte[16384]; written = 0;
        while (true)
        {
            int requested = (int)Math.Min(buffer.Length, maximum - written + BlockAlign);
            int count = input.Read(buffer, 0, requested);
            if (count == 0) return;
            if (count < 0 || count > requested || count % BlockAlign != 0) throw new DecodeException("CustomMp3Failed");
            if (written + count > maximum) throw new DecodeException("CustomMp3Limit");
            output.Write(buffer, 0, count); written += count;
        }
    }
}
