// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

using System.Buffers;
using System.Security.Cryptography;

namespace PDownloader.Core.Download.Infrastructure;

public sealed record FileHashResult(
    string Md5,
    string Sha1,
    string Sha256);

internal sealed class FileHashAccumulator : IDisposable
{
    private readonly IncrementalHash _md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
    private readonly IncrementalHash _sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
    private readonly IncrementalHash _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _completed;

    public void AppendData(byte[] buffer, int count)
    {
        ThrowIfCompleted();

        if (count <= 0)
        {
            return;
        }

        _md5.AppendData(buffer, 0, count);
        _sha1.AppendData(buffer, 0, count);
        _sha256.AppendData(buffer, 0, count);
    }

    public FileHashResult Complete()
    {
        ThrowIfCompleted();
        _completed = true;

        return new FileHashResult(
            Convert.ToHexString(_md5.GetHashAndReset()),
            Convert.ToHexString(_sha1.GetHashAndReset()),
            Convert.ToHexString(_sha256.GetHashAndReset()));
    }

    public void Dispose()
    {
        _completed = true;
        _md5.Dispose();
        _sha1.Dispose();
        _sha256.Dispose();
    }

    private void ThrowIfCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                "The hash object cannot be used further after completion.");
        }
    }
}

public static class FileHashCalculator
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<FileHashResult> ComputeAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var accumulator = new FileHashAccumulator();
        await AppendFileAsync(
            accumulator,
            filePath,
            maxBytes: null,
            cancellationToken: cancellationToken);
        return accumulator.Complete();
    }

    internal static Task AppendFilePrefixAsync(
        FileHashAccumulator accumulator,
        string filePath,
        long byteCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accumulator);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

        return AppendFileAsync(
            accumulator,
            filePath,
            byteCount,
            cancellationToken);
    }

    private static async Task AppendFileAsync(
        FileHashAccumulator accumulator,
        string filePath,
        long? maxBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            long remaining = maxBytes ?? long.MaxValue;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = await stream.ReadAsync(
                    buffer.AsMemory(0, requested),
                    cancellationToken);

                if (read == 0)
                {
                    if (maxBytes.HasValue && remaining > 0)
                    {
                        throw new EndOfStreamException(
                            $"Unable to restore hash state: '{filePath}' is shorter than the " +
                            $"checkpoint to be read ({maxBytes.Value} B).");
                    }

                    break;
                }

                accumulator.AppendData(buffer, read);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
