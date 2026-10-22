using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace HiveoryPet;

/// <summary>Two soft sine blips made on the fly (no sound files to ship), played by Windows' own media player.</summary>
internal static class Chime
{
    private static readonly byte[] FinishedWav = Wav((1318.5, 0.07), (1760.0, 0.12));
    private static readonly byte[] NeedsYouWav = Wav((880.0, 0.08), (0, 0.05), (880.0, 0.1));
    private static readonly MediaPlayer Player = new() { Volume = 0.5 };

    public static void Finished() => Play(FinishedWav);

    public static void NeedsYou() => Play(NeedsYouWav);

    private static async void Play(byte[] wav)
    {
        try
        {
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(wav.AsBuffer());
            stream.Seek(0);
            Player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
            Player.Play();
        }
        catch (Exception e)
        {
            Log.Write("Chime", e); // no audio device: stay quiet
        }
    }

    /// <summary>16-bit mono PCM; each note fades in and out so it never clicks.</summary>
    private static byte[] Wav(params (double Hz, double Seconds)[] notes)
    {
        const int rate = 44100;
        var samples = new List<short>();
        foreach (var (hz, seconds) in notes)
        {
            var count = (int)(rate * seconds);
            for (var i = 0; i < count; i++)
            {
                var envelope = Math.Min(1, Math.Min(i, count - i) / (rate * 0.012));
                samples.Add((short)(Math.Sin(2 * Math.PI * hz * i / rate) * envelope * 0.22 * short.MaxValue));
            }
        }

        using var memory = new MemoryStream();
        using var w = new BinaryWriter(memory);
        w.Write("RIFF"u8);
        w.Write(36 + (samples.Count * 2));
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Count * 2);
        foreach (var s in samples)
        {
            w.Write(s);
        }

        w.Flush();
        return memory.ToArray();
    }
}
