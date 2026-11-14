using System.Buffers.Binary;
using System.Numerics;

namespace Shadowline.Core.Tracks;

public sealed class BitTrack
{
    private readonly ulong[] words;

    public BitTrack(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
        words = new ulong[(length + 63) >> 6];
    }

    public int Length { get; }

    public bool this[int index]
    {
        get
        {
            ValidateIndex(index);
            return ((words[index >> 6] >> (index & 63)) & 1UL) != 0;
        }
        set
        {
            ValidateIndex(index);
            var mask = 1UL << (index & 63);
            if (value)
            {
                words[index >> 6] |= mask;
            }
            else
            {
                words[index >> 6] &= ~mask;
            }
        }
    }

    public int CountSet()
    {
        var total = 0;
        foreach (var word in words)
        {
            total += BitOperations.PopCount(word);
        }

        return total;
    }

    public double Fraction() => Length == 0 ? 0.0 : (double)CountSet() / Length;

    public IEnumerable<Run> Runs(bool value)
    {
        var start = -1;
        for (var i = 0; i < Length; i++)
        {
            if (this[i] == value)
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                yield return new Run(start, i - start);
                start = -1;
            }
        }

        if (start >= 0)
        {
            yield return new Run(start, Length - start);
        }
    }

    public int LongestRun(bool value)
    {
        var longest = 0;
        foreach (var run in Runs(value))
        {
            longest = Math.Max(longest, run.Length);
        }

        return longest;
    }

    public BitTrack Slice(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > Length || length > Length - start)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var slice = new BitTrack(length);
        for (var i = 0; i < length; i++)
        {
            slice[i] = this[start + i];
        }

        return slice;
    }

    public BitTrack And(BitTrack other) => Combine(other, static (a, b) => a & b);

    public BitTrack Or(BitTrack other) => Combine(other, static (a, b) => a | b);

    public BitTrack Not()
    {
        var result = new BitTrack(Length);
        for (var i = 0; i < words.Length; i++)
        {
            result.words[i] = ~words[i];
        }

        result.ClearUnusedBits();
        return result;
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[words.Length * 8];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8, 8), words[i]);
        }

        return bytes;
    }

    public static BitTrack FromBytes(ReadOnlySpan<byte> bytes, int length)
    {
        var track = new BitTrack(length);
        if (bytes.Length != track.words.Length * 8)
        {
            throw new ArgumentException("Byte count does not match track length", nameof(bytes));
        }

        for (var i = 0; i < track.words.Length; i++)
        {
            track.words[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i * 8, 8));
        }

        track.ClearUnusedBits();
        return track;
    }

    private BitTrack Combine(BitTrack other, Func<ulong, ulong, ulong> operation)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Length != Length)
        {
            throw new ArgumentException("Tracks must have equal length", nameof(other));
        }

        var result = new BitTrack(Length);
        for (var i = 0; i < words.Length; i++)
        {
            result.words[i] = operation(words[i], other.words[i]);
        }

        return result;
    }

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private void ClearUnusedBits()
    {
        var used = Length & 63;
        if (used != 0)
        {
            words[^1] &= (1UL << used) - 1UL;
        }
    }
}
