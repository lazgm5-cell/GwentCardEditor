using System.Text.Json;
using GwentAudioDecode;
using NAudio.Wave;

var passed = new List<string>();
string root = Path.Combine(Path.GetTempPath(), "GwentMp3Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
void Reject(Action action, string name)
{
    bool rejected = false; try { action(); } catch { rejected = true; }
    Check(rejected, name);
}
try
{
    string input = Path.Combine(root, "silence.mp3"), output = Path.Combine(root, "decoded.wav");
    // MPEG-1 Layer III, 128 kbps, 44.1 kHz stereo; zero side-information encodes silence.
    byte[] frame = new byte[417]; frame[0] = 0xff; frame[1] = 0xfb; frame[2] = 0x90;
    File.WriteAllBytes(input, Enumerable.Range(0, 20).SelectMany(_ => frame).ToArray());
    byte[] original = File.ReadAllBytes(input);
    var info = Mp3Decoder.Decode(input, output);
    Check(info.SampleRate == 48000 && info.Channels == 2, "MP3 decoded to 48 kHz stereo");
    Check(info.Seconds > .3 && info.Seconds < .8, "decoded MP3 duration bounded");
    using (var reader = new WaveFileReader(output))
    {
        Check(reader.WaveFormat.BitsPerSample == 16 && reader.WaveFormat.Encoding == WaveFormatEncoding.Pcm, "output PCM16");
        byte[] samples = new byte[reader.Length]; int read = reader.Read(samples, 0, samples.Length);
        Check(read == samples.Length && samples.All(b => b == 0), "synthetic silent MP3 decoded correctly");
    }
    Check(original.SequenceEqual(File.ReadAllBytes(input)), "source MP3 unchanged");
    Check(!File.Exists(output + ".mp3-tmp"), "no staging file after success");
    string mono = Path.Combine(root, "mono.mp3"); byte[] monoFrame = (byte[])frame.Clone(); monoFrame[3] = 0xc0;
    File.WriteAllBytes(mono, Enumerable.Range(0, 20).SelectMany(_ => monoFrame).ToArray());
    var monoInfo = Mp3Decoder.Decode(mono, Path.Combine(root, "mono.wav"));
    Check(monoInfo.Channels == 2 && monoInfo.SampleRate == 48000, "mono MP3 converted to stereo");
    string low = Path.Combine(root, "low-rate.mp3"); byte[] lowFrame = new byte[208]; lowFrame[0] = 0xff; lowFrame[1] = 0xf3; lowFrame[2] = 0x80; lowFrame[3] = 0xc0;
    File.WriteAllBytes(low, Enumerable.Range(0, 20).SelectMany(_ => lowFrame).ToArray());
    var lowInfo = Mp3Decoder.Decode(low, Path.Combine(root, "low-rate.wav"));
    Check(lowInfo.Channels == 2 && lowInfo.SampleRate == 48000 && lowInfo.Seconds > .3, "22.05 kHz MP3 resampled");
    string variable = Path.Combine(root, "variable.mp3"); byte[] largeFrame = new byte[522]; Array.Copy(frame, largeFrame, 4); largeFrame[2] = 0xa0;
    File.WriteAllBytes(variable, Enumerable.Range(0, 20).SelectMany(n => n % 2 == 0 ? frame : largeFrame).ToArray());
    var variableInfo = Mp3Decoder.Decode(variable, Path.Combine(root, "variable.wav"));
    Check(variableInfo.Sha256 == info.Sha256, "variable bitrate MP3 decoded");
    string tagged = Path.Combine(root, "tagged.mp3");
    byte[] tag = new byte[26]; Array.Copy(System.Text.Encoding.ASCII.GetBytes("ID3"), tag, 3); tag[3] = 3; tag[9] = 16;
    Array.Copy(System.Text.Encoding.ASCII.GetBytes("PRIVATE METADATA"), 0, tag, 10, 16);
    byte[] endTag = new byte[128]; Array.Copy(System.Text.Encoding.ASCII.GetBytes("TAG"), endTag, 3);
    File.WriteAllBytes(tagged, tag.Concat(original).Concat(endTag).ToArray());
    var taggedInfo = Mp3Decoder.Decode(tagged, Path.Combine(root, "tagged.wav"));
    Check(taggedInfo.Sha256 == info.Sha256, "ID3 metadata excluded from normalized WAV");
    string unicode = Path.Combine(root, "\u0442\u0435\u0441\u0442 audio.MP3"); File.Copy(input, unicode);
    Check(Mp3Decoder.Decode(unicode, Path.Combine(root, "unicode.wav")).Sha256 == info.Sha256, "Unicode paths and uppercase extension supported");
    Reject(() => Mp3Decoder.Decode(input, output), "existing output rejected");
    Reject(() => Mp3Decoder.Decode(input, input), "source overwrite rejected");
    Check(original.SequenceEqual(File.ReadAllBytes(input)), "source preserved after overwrite attempts");
    string stageOutput = Path.Combine(root, "stage.wav"); File.WriteAllText(stageOutput + ".mp3-tmp", "foreign content");
    Reject(() => Mp3Decoder.Decode(input, stageOutput), "foreign staging file rejected");
    Check(File.ReadAllText(stageOutput + ".mp3-tmp") == "foreign content", "foreign staging file preserved");
    string broken = Path.Combine(root, "broken.mp3"); File.WriteAllText(broken, "not audio");
    Reject(() => Mp3Decoder.Decode(broken, Path.Combine(root, "broken.wav")), "malformed MP3 rejected");
    Check(!File.Exists(Path.Combine(root, "broken.wav")), "malformed input leaves no output");
    string empty = Path.Combine(root, "empty.mp3"); File.WriteAllBytes(empty, []);
    Reject(() => Mp3Decoder.Decode(empty, Path.Combine(root, "empty.wav")), "empty MP3 rejected");
    string large = Path.Combine(root, "large.mp3"); using (var file = File.Create(large)) file.SetLength(50L * 1024 * 1024 + 1);
    Reject(() => Mp3Decoder.Decode(large, Path.Combine(root, "large.wav")), "oversized MP3 rejected before decoding");
    using var exact = new MemoryStream(); Mp3Decoder.WriteBounded(new FakePcm(16), exact, 16, out long written);
    Check(written == 16 && exact.Length == 16, "exact duration limit accepted");
    Reject(() => Mp3Decoder.WriteBounded(new FakePcm(20), new MemoryStream(), 16, out _), "over-limit PCM rejected");
    Reject(() => Mp3Decoder.WriteBounded(new FakePcm(16, true), new MemoryStream(), 16, out _), "unaligned PCM rejected");
    Check(!File.Exists(Path.Combine(root, "large.wav")), "oversized input leaves no output");
    string longInput = Path.Combine(root, "long.mp3"), longOutput = Path.Combine(root, "long.wav");
    File.WriteAllBytes(longInput, Enumerable.Range(0, 5000).SelectMany(_ => frame).ToArray());
    Reject(() => Mp3Decoder.Decode(longInput, longOutput), "actual MP3 over 120 seconds rejected");
    Check(!File.Exists(longOutput) && !File.Exists(longOutput + ".mp3-tmp"), "overlong decode leaves no output or staging file");
    double longDuration = Mp3Decoder.Probe(longInput);
    Check(longDuration > 120 && longDuration < 140, "long MP3 duration inspected without output");
    var segment = Mp3Decoder.Decode(longInput, longOutput, 120, 121);
    Check(segment.Seconds == 1 && segment.Channels == 2, "one-second range extracted after two minutes");
    Check(!File.Exists(longOutput + ".mp3-tmp"), "range extraction removes staging file");
    foreach (var bounds in new[] { (-1d, 1d), (1d, 1d), (0d, 121d), (0d, double.NaN), (0d, double.PositiveInfinity), (130d, 140d) })
    {
        string invalidOutput = Path.Combine(root, "invalid-range.wav");
        Reject(() => Mp3Decoder.Decode(longInput, invalidOutput, bounds.Item1, bounds.Item2), "invalid MP3 range rejected " + bounds);
        Check(!File.Exists(invalidOutput) && !File.Exists(invalidOutput + ".mp3-tmp"), "invalid range leaves no files " + bounds);
    }
    using var rangeBytes = new MemoryStream();
    Mp3Decoder.WriteSegment(new SequencePcm(), rangeBytes, 1d / 48000, 4d / 48000, out long rangeWritten);
    Check(rangeWritten == 12 && rangeBytes.ToArray().SequenceEqual(Enumerable.Range(4, 12).Select(n => (byte)n)), "range skips exact PCM frames");
    Reject(() => Mp3Decoder.WriteSegment(new FakePcm(16), new MemoryStream(), 0, 1, out _), "truncated selected range rejected");
    Reject(() => Mp3Decoder.WriteSegment(new FakePcm(16, true), new MemoryStream(), 0, .1, out _), "unaligned selected range rejected");
    if (args.Length > 1)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(args[1])) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        foreach (string argument in new[] { "--source", broken, "--output", Path.Combine(root, "worker-error.wav") }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("Worker did not start");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree:true); process.WaitForExit(); throw new Exception("Worker timeout"); }
        Check(process.ExitCode != 0 && stderr.Result.Contains("CustomMp3Failed"), "separate worker reports malformed MP3 error");
        Check(string.IsNullOrWhiteSpace(stdout.Result) && !File.Exists(Path.Combine(root, "worker-error.wav")), "failed worker creates no result");
    }
    var report = JsonSerializer.Serialize(new { Passed=true, Count=passed.Count, CodecTested=true, AudioPlayed=false, Tests=passed }, new JsonSerializerOptions { WriteIndented=true });
    if (args.Length > 0) File.WriteAllText(args[0], report);
    Console.WriteLine(report);
}
catch (Exception e)
{
    Console.Error.WriteLine(e); Environment.ExitCode = 1;
    if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed=false, Count=passed.Count, Error=e.ToString() }));
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "GwentMp3Tests-"), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
}

sealed class FakePcm(int remaining, bool malformed = false) : IWaveProvider
{
    public WaveFormat WaveFormat => new(48000, 16, 2);
    public int Read(byte[] buffer, int offset, int count)
    {
        if (malformed) return 3;
        int result = Math.Min(remaining, count); remaining -= result; Array.Clear(buffer, offset, result); return result;
    }
}

sealed class SequencePcm : IWaveProvider
{
    private int position;
    public WaveFormat WaveFormat => new(48000, 16, 2);
    public int Read(byte[] buffer, int offset, int count)
    {
        for (int index = 0; index < count; index++) buffer[offset + index] = (byte)(position++ % 256);
        return count;
    }
}
