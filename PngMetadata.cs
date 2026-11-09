using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SimpleViewer;

/// <summary>Reads the A1111/Forge "parameters" text chunk from a PNG without decoding the image.</summary>
internal static class PngMetadata
{
    static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    const string Keyword = "parameters";
    static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string? ReadParameters(Stream s)
    {
        try
        {
            var head = new byte[8];
            s.ReadExactly(head);
            if (!head.AsSpan().SequenceEqual(Signature)) return null;
            while (true)
            {
                s.ReadExactly(head);
                uint len = BinaryPrimitives.ReadUInt32BigEndian(head);
                string type = Encoding.ASCII.GetString(head, 4, 4);
                if (type == "IEND" || len > 64 * 1024 * 1024) return null;
                if (type is "tEXt" or "zTXt" or "iTXt")
                {
                    var data = new byte[len];
                    s.ReadExactly(data);
                    string? text = DecodeChunk(type, data);
                    if (text != null) return text;
                }
                else s.Seek(len, SeekOrigin.Current); // skip image data without reading it
                s.Seek(4, SeekOrigin.Current);        // CRC
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { return null; }
    }

    static string? DecodeChunk(string type, byte[] d)
    {
        int p = Array.IndexOf(d, (byte)0);
        if (p < 0 || Encoding.Latin1.GetString(d, 0, p) != Keyword) return null;
        p++;
        switch (type)
        {
            case "tEXt":
                return Text(d.AsSpan(p).ToArray());
            case "zTXt":
                return Text(Inflate(d, p + 1)); // skip compression method byte
            default: // iTXt: flag, method, language\0, translated keyword\0, UTF-8 text
                bool compressed = d[p] == 1;
                p = Array.IndexOf(d, (byte)0, p + 2) + 1;
                if (p <= 0) return null;
                p = Array.IndexOf(d, (byte)0, p) + 1;
                if (p <= 0) return null;
                return Encoding.UTF8.GetString(compressed ? Inflate(d, p) : d.AsSpan(p).ToArray());
        }
    }

    // tEXt/zTXt are Latin-1 by spec, but some tools write UTF-8: prefer UTF-8 if it decodes cleanly.
    static string Text(byte[] b)
    {
        try { return StrictUtf8.GetString(b); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(b); }
    }

    static byte[] Inflate(byte[] d, int offset)
    {
        using var z = new ZLibStream(new MemoryStream(d, offset, d.Length - offset), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Text before "Negative prompt:" (or before the "Steps:" line if there is no negative prompt).</summary>
    public static string Prompt(string parameters)
    {
        int i = parameters.IndexOf("Negative prompt:", StringComparison.Ordinal);
        if (i < 0) i = parameters.LastIndexOf("\nSteps: ", StringComparison.Ordinal);
        return (i < 0 ? parameters : parameters[..i]).Trim();
    }

    public static string? Seed(string parameters)
    {
        var m = Regex.Match(parameters, @"(?:^|[\s,])Seed:\s*(-?\d+)", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }
}
