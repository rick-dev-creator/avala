using Avala.Components.UI.Pictures;
using Avala.Testing.UI;

namespace Avala.Components.UI.Tests;

public sealed class PictureCacheScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task APictureIsDecodedAtTheWidthItIsShownAtButNeverWiderThanItselfAsync() =>
        ui.RunAsync(async () =>
        {
            var pictures = new PictureCache();

            var narrow = Pngs.Of(300, 20);

            using var wide = await pictures.DecodeAsync(Pngs.Of(3000, 20), 520, Cancellation);
            using var thumbnail = await pictures.DecodeAsync(narrow, 520, Cancellation);
            using var sheet = await pictures.DecodeAsync(narrow, 1600, Cancellation);

            Assert.Equal(520, wide.Width);
            Assert.Same(((DecodedPicture)thumbnail).Bitmap, ((DecodedPicture)sheet).Bitmap);
        }, Cancellation);

    [Fact]
    public Task TheSamePictureShownTwiceSharesOneDecodedBitmapAsync() =>
        ui.RunAsync(async () =>
        {
            var pictures = new PictureCache();
            var png = Pngs.Of(800, 20);

            using var thumbnail = await pictures.DecodeAsync(png, 520, Cancellation);
            using var again = await pictures.DecodeAsync([.. png], 520, Cancellation);

            Assert.Same(((DecodedPicture)thumbnail).Bitmap, ((DecodedPicture)again).Bitmap);
            Assert.Equal(1, pictures.Live);
        }, Cancellation);

    [Fact]
    public Task PicturesNoLongerShownAreDisposedOnceTheyExceedTheBudgetWhileShownOnesStayAsync() =>
        ui.RunAsync(async () =>
        {
            var pictures = new PictureCache(budget: 1);

            var first = await pictures.DecodeAsync(Pngs.Of(800, 20, 1), 520, Cancellation);
            using var second = await pictures.DecodeAsync(Pngs.Of(800, 20, 2), 520, Cancellation);
            var bothShown = pictures.Live;
            first.Dispose();

            Assert.Equal(2, bothShown);
            Assert.Equal((1, pictures.LeasedBytes), (pictures.Live, pictures.LiveBytes));
        }, Cancellation);
}
