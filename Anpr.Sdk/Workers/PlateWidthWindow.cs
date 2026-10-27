namespace Anpr.Sdk.Workers;

/// <summary>
/// Son N plaka genişliğinin halka tamponu (iş parçacığı güvenli) — <see cref="ICameraStatus.RecentPlateWidths"/>
/// buradan hesaplanır. Pozitif olmayan / bilinmeyen değerler (<c>null</c>) yok sayılır.
/// </summary>
internal sealed class PlateWidthWindow
{
    private readonly int[] _values;
    private int _count, _next;
    private readonly object _lock = new();

    public PlateWidthWindow(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _values = new int[capacity];
    }

    public int Capacity => _values.Length;

    public void Add(int? widthPx)
    {
        if (widthPx is not > 0) return;
        lock (_lock)
        {
            _values[_next] = widthPx.Value;
            _next = (_next + 1) % _values.Length;
            if (_count < _values.Length) _count++;
        }
    }

    public PlateWidthStats? Stats()
    {
        lock (_lock) return PlateWidthStats.From(_values.Take(_count));
    }
}
