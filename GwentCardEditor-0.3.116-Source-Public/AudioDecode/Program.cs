using System.Text;
using System.Text.Json;
using NAudio;

namespace GwentAudioDecode;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 3 && args[0] == "--probe" && args[1] == "--source")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { Seconds = Mp3Decoder.Probe(args[2]) }));
                return 0;
            }
            if (args.Length is not (4 or 8) || args[0] != "--source" || args[2] != "--output") throw new DecodeException("CustomMp3Failed");
            double? start = null, end = null;
            if (args.Length == 8)
            {
                if (args[4] != "--start" || args[6] != "--end"
                    || !double.TryParse(args[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double first)
                    || !double.TryParse(args[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double last)) throw new DecodeException("CustomAudioRange");
                start = first; end = last;
            }
            var result = Mp3Decoder.Decode(args[1], args[3], start, end);
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Exception e)
        {
            string code = e is DecodeException ? e.Message : e is MmException ? "CustomMp3Codec" : "CustomMp3Failed";
            Console.Error.WriteLine(code); Console.Error.WriteLine(e);
            return 1;
        }
    }
}
