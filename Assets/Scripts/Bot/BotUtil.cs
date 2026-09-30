#if UNITY_EDITOR || BOT_RUNNER
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Every seed the balance bot hands the game, derived from one base seed so a whole batch is
/// reproducible from the number written in its job.json.
///
/// SplitMix64 rather than anything built in: string.GetHashCode is randomised per process in modern
/// .NET, and System.Random's own seeding is an implementation detail that has changed between runtimes.
/// A seed that reproduces only on the machine that recorded it is not a seed.
///
/// The level seed depends only on (run seed, level), never on anything the bot did before it - that is
/// what puts every profile in a batch in front of the same encounter on the same level, so their
/// results can be compared seed for seed.
/// </summary>
public static class BotSeeds
{
    public static ulong Mix(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    private static ulong U(int value) => unchecked((ulong)(uint)value);

    private static int Fold(ulong x) => unchecked((int)(x ^ (x >> 32)));

    public static int RunSeed(int baseSeed, int runIndex) => Fold(Mix(Mix(U(baseSeed)) ^ U(runIndex)));

    public static int LevelSeed(int runSeed, int level) =>
        Fold(Mix(Mix(U(runSeed) ^ 0x4C4556454CUL) ^ U(level)));

    public static int DecisionSeed(int runSeed, int level, int round, int step)
    {
        ulong x = Mix(U(runSeed) ^ 0x4445434953UL);
        x = Mix(x ^ U(level));
        x = Mix(x ^ U(round));
        x = Mix(x ^ U(step));

        return Fold(x);
    }

    /// FNV-1a, 64-bit - the fingerprint and digest hash. Stable across processes and machines.
    public static ulong Fnv1a64(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;

            if (text == null) { return hash; }

            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash;
        }
    }

    public static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
}

/// <summary>
/// Formatting and file helpers for the bot's outputs. Everything numeric goes through InvariantCulture:
/// a Windows locale with a decimal comma would otherwise write "1,5" into a CSV and split the column.
/// </summary>
public static class BotText
{
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// For CSVs only: Excel reads a CSV as the machine's ANSI code page unless it opens with a UTF-8 byte
    /// order mark, which turns every "—" in a skip label into mojibake.
    public static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static string F(float value, int decimals = 2) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? "0"
            : value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    public static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// A folder- and file-safe version of a name: letters, digits, '-' and '_' kept, everything else
    /// collapsed to '-'.
    public static string Slug(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return "unnamed"; }

        StringBuilder sb = new();

        foreach (char c in text.Trim())
        {
            bool keep = char.IsLetterOrDigit(c) || c == '-' || c == '_';

            if (keep) { sb.Append(c); }
            else if (sb.Length > 0 && sb[^1] != '-') { sb.Append('-'); }
        }

        string slug = sb.ToString().Trim('-');

        return slug.Length > 0 ? slug : "unnamed";
    }

    /// One CSV field, quoted only when it has to be.
    public static string Csv(string field)
    {
        if (field == null) { return string.Empty; }

        bool quote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;

        return quote ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }

    /// Writes through a temp file and a move, so a reader polling the file - the Editor bridge reading
    /// status.json, a PowerShell script reading a shard's status - never sees half of one.
    public static void WriteAtomic(string path, string content, Encoding encoding = null)
    {
        string directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }

        string temp = path + ".tmp";

        File.WriteAllText(temp, content, encoding ?? Utf8);

        if (File.Exists(path)) { File.Delete(path); }

        File.Move(temp, path);
    }
}
#endif
