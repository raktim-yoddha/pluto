using System.IO;
using System.Media;

namespace HiveoryPet.Ui;

/// <summary>Two soft sine blips, made on the fly (no sound files to ship): up for "finished", a double ping for "needs you".</summary>
public static class Chime
{
    private static readonly byte[] FinishedWav = Wav((1318.5, 0.07), (1760.0, 0.11));
    private static readonly byte[] NeedsYouWav = Wav((880.0, 0.08), (0, 0.05), (880.0, 0.1));

    public static void Finished() => Play(FinishedWav);

    public static void NeedsYou() => Play(NeedsYouWav);

    private static void Play(byte[] wav)
    {
        try
        {
            using var player = new SoundPlayer(new MemoryStream(wav));
            player.Play(); // async; the player keeps its own copy
        }
        catch (InvalidOperationException)
        {
            // no audio device
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

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
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
        return stream.ToArray();
    }
}
