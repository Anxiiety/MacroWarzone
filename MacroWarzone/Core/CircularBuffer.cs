using System;
using System.Linq;

namespace MacroWarzone.Core;

/// <summary>
/// Circular buffer thread-safe per samples realtime.
/// FIFO: quando pieno, sovrascrive elemento più vecchio.
/// </summary>
/// <typeparam name="T">Tipo elemento (es: RecoilSample)</typeparam>
public sealed class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _capacity;
    private int _writeIndex;
    private int _count;
    private readonly object _lock = new();

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be > 0", nameof(capacity));

        _capacity = capacity;
        _buffer = new T[capacity];
        _writeIndex = 0;
        _count = 0;
    }

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    public int Capacity => _capacity;

    public bool IsFull
    {
        get { lock (_lock) return _count == _capacity; }
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_writeIndex] = item;
            _writeIndex = (_writeIndex + 1) % _capacity;

            if (_count < _capacity)
                _count++;
        }
    }

    public T[] GetSnapshot()
    {
        lock (_lock)
        {
            if (_count == 0)
                return Array.Empty<T>();

            var snapshot = new T[_count];

            if (_count < _capacity)
            {
                Array.Copy(_buffer, 0, snapshot, 0, _count);
            }
            else
            {
                int firstPart = _capacity - _writeIndex;
                Array.Copy(_buffer, _writeIndex, snapshot, 0, firstPart);
                Array.Copy(_buffer, 0, snapshot, firstPart, _writeIndex);
            }

            return snapshot;
        }
    }

    public T[] GetLast(int n)
    {
        lock (_lock)
        {
            if (n <= 0 || _count == 0)
                return Array.Empty<T>();

            int actualCount = Math.Min(n, _count);
            var result = new T[actualCount];

            var all = GetSnapshot();
            Array.Copy(all, all.Length - actualCount, result, 0, actualCount);

            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _writeIndex = 0;
            _count = 0;
        }
    }

    public double Average(Func<T, double> selector)
    {
        lock (_lock)
        {
            if (_count == 0)
                return 0.0;

            var snapshot = GetSnapshot();
            return snapshot.Average(selector);
        }
    }

    public double Sum(Func<T, double> selector)
    {
        lock (_lock)
        {
            if (_count == 0)
                return 0.0;

            var snapshot = GetSnapshot();
            return snapshot.Sum(selector);
        }
    }
}
