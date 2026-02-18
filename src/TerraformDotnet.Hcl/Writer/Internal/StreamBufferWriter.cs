using System.Buffers;

namespace TerraformDotnet.Hcl.Writer.Internal;

/// <summary>
/// Wraps a <see cref="Stream"/> as an <see cref="IBufferWriter{T}"/>,
/// using pooled arrays for buffering.
/// </summary>
internal sealed class StreamBufferWriter : IBufferWriter<byte>, IDisposable
{
    private readonly Stream _stream;
    private byte[] _buffer;
    private int _index;

    /// <summary>
    /// Initializes a new <see cref="StreamBufferWriter"/> over the given stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="initialCapacity">The initial buffer capacity.</param>
    public StreamBufferWriter(Stream stream, int initialCapacity = 4096)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _index = 0;
    }

    /// <inheritdoc />
    public void Advance(int count)
    {
        _index += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_index);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_index);
    }

    /// <summary>
    /// Flushes the buffer contents to the underlying stream.
    /// </summary>
    public void Flush()
    {
        if (_index > 0)
        {
            _stream.Write(_buffer, 0, _index);
            _index = 0;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Flush();
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private void EnsureCapacity(int sizeHint)
    {
        int requiredSize = Math.Max(sizeHint, 1);
        if (_buffer.Length - _index < requiredSize)
        {
            Flush();
            if (_buffer.Length < requiredSize)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = ArrayPool<byte>.Shared.Rent(requiredSize);
            }
        }
    }
}
