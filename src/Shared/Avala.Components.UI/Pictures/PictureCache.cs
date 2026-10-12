using System.Security.Cryptography;
using Avala.Components.Pictures;
using Avala.Sdk;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Avala.Components.UI.Pictures;

public sealed class PictureCache(long budget) : IPictureDecoder
{
    public const long StandardBudget = 64L * 1024 * 1024;

    private readonly Dictionary<(UInt128 Content, int Width), Entry> entries = [];
    private readonly LinkedList<Entry> idle = [];

    public PictureCache()
        : this(StandardBudget)
    {
    }

    public long Budget => budget;

    public int Live { get; private set; }

    public long LiveBytes { get; private set; }

    public long LeasedBytes => entries.Values.Where(entry => entry.Leases > 0).Sum(entry => entry.Bytes);

    public async Task<IPicture> DecodeAsync(byte[] encoded, int width, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();

        if (encoded.Length == 0 || width <= 0)
        {
            return NoPicture.Instance;
        }

        var natural = PictureHeader.Width(encoded);
        var target = natural.Match(found => Math.Min(found, width), () => width);
        var shrinks = natural.Match(found => found > width, () => true);
        var key = (await Task.Run(() => Content(encoded), cancellationToken), target);

        if (!entries.TryGetValue(key, out var entry))
        {
            entry = new Entry(key, Task.Run(() => Decode(encoded, target, shrinks), CancellationToken.None));
            entries[key] = entry;
        }

        Lease(entry);
        Count(entry, await entry.Decoding);

        if (cancellationToken.IsCancellationRequested)
        {
            Release(entry);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return entry.Picture.Match<IPicture>(bitmap => new DecodedPicture(bitmap, () => Release(entry)), () =>
        {
            Release(entry);

            return NoPicture.Instance;
        });
    }

    private static UInt128 Content(byte[] encoded)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(encoded, hash);

        return new UInt128(BitConverter.ToUInt64(hash), BitConverter.ToUInt64(hash[8..]));
    }

    private static Option<Bitmap> Decode(byte[] encoded, int width, bool shrinks)
    {
        try
        {
            using var stream = new MemoryStream(encoded, writable: false);

            return shrinks ? Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality) : new Bitmap(stream);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return Option<Bitmap>.None;
        }
    }

    private void Lease(Entry entry)
    {
        entry.Idle.Match(
            node =>
            {
                idle.Remove(node);

                return true;
            },
            () => false);
        entry.Idle = Option<LinkedListNode<Entry>>.None;
        entry.Leases++;
    }

    private void Count(Entry entry, Option<Bitmap> decoded)
    {
        if (entry.IsCounted)
        {
            return;
        }

        entry.IsCounted = true;
        entry.Picture = decoded;
        decoded.Match(
            bitmap =>
            {
                entry.Bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
                Live++;
                LiveBytes += entry.Bytes;

                return true;
            },
            () => entries.Remove(entry.Key));
    }

    private void Release(Entry entry)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Release(entry));

            return;
        }

        entry.Leases--;

        if (entry.Leases == 0 && entries.ContainsKey(entry.Key))
        {
            entry.Idle = idle.AddLast(entry);
        }

        Trim();
    }

    private void Trim()
    {
        while (LiveBytes > budget && idle.First is { } oldest)
        {
            idle.RemoveFirst();
            var entry = oldest.Value;
            entry.Idle = Option<LinkedListNode<Entry>>.None;
            entries.Remove(entry.Key);
            Live--;
            LiveBytes -= entry.Bytes;
            entry.Picture.Match(
                bitmap =>
                {
                    bitmap.Dispose();

                    return true;
                },
                () => false);
            entry.Picture = Option<Bitmap>.None;
        }
    }

    private sealed class Entry((UInt128 Content, int Width) key, Task<Option<Bitmap>> decoding)
    {
        public (UInt128 Content, int Width) Key => key;

        public Task<Option<Bitmap>> Decoding => decoding;

        public Option<Bitmap> Picture { get; set; }

        public int Leases { get; set; }

        public long Bytes { get; set; }

        public bool IsCounted { get; set; }

        public Option<LinkedListNode<Entry>> Idle { get; set; }
    }
}
