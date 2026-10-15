using QRCoder;

namespace Avala.Components.QrCodes;

public sealed record QrModules(int Size, IReadOnlyList<bool> Dark)
{
    public static QrModules None { get; } = new(0, []);

    public bool IsEmpty => Size == 0;

    public static QrModules Of(string text)
    {
        if (text.Length == 0)
        {
            return None;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;

        return new QrModules(matrix.Count, [.. matrix.SelectMany(row => Enumerable.Range(0, matrix.Count).Select(column => row[column]))]);
    }

    public bool IsDark(int row, int column) => Dark[(row * Size) + column];
}
