using Avala.Components.Virtualizing;

namespace Avala.Components.UI.Virtualizing;

internal sealed class HeightEstimates
{
    private const double Unknown = 48;
    private const double Lead = 90;
    private const double PerCharacterArea = 215;
    private const double Least = 16;
    private const int Enough = 20;

    private static readonly double[] Prior = [0.5, 4];

    private readonly Dictionary<Type, Kind> kinds = [];

    public double Of(object item, double width)
    {
        var text = Text(item, width);
        var estimate = kinds.TryGetValue(item.GetType(), out var kind) ? kind.Predict(text)
            : item is ITextSized ? Lead + (PerCharacterArea * text)
            : Unknown;

        return Math.Max(Least, estimate);
    }

    public void Learn(object item, double width, double height)
    {
        if (!kinds.TryGetValue(item.GetType(), out var kind))
        {
            kind = new Kind();
            kinds[item.GetType()] = kind;

            if (item is ITextSized)
            {
                foreach (var x in Prior)
                {
                    kind.Add(x, Lead + (PerCharacterArea * x));
                }
            }
        }

        if (kind.Count < Enough)
        {
            kind.Add(Text(item, width), height);
        }
    }

    private static double Text(object item, double width) =>
        item is ITextSized sized && width > 0 ? sized.TextLength / width : 0;

    private sealed class Kind
    {
        private int count;
        private double sumX;
        private double sumY;
        private double sumXx;
        private double sumXy;

        public int Count => count;

        public void Add(double x, double y)
        {
            count++;
            sumX += x;
            sumY += y;
            sumXx += x * x;
            sumXy += x * y;
        }

        public double Predict(double x)
        {
            var meanX = sumX / count;
            var meanY = sumY / count;
            var spread = (sumXx / count) - (meanX * meanX);
            var slope = spread > 1e-9 ? Math.Max(0, ((sumXy / count) - (meanX * meanY)) / spread) : PerCharacterArea;

            return meanY + (slope * (x - meanX));
        }
    }
}
