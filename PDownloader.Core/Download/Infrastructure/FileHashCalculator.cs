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

public static class FileHashCalculator
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<FileHashResult> ComputeAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (true)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(0, BufferSize),
                    cancellationToken);

                if (read == 0)
                {
                    break;
                }

                md5.AppendData(buffer, 0, read);
                sha1.AppendData(buffer, 0, read);
                sha256.AppendData(buffer, 0, read);
            }

            return new FileHashResult(
                Convert.ToHexString(md5.GetHashAndReset()),
                Convert.ToHexString(sha1.GetHashAndReset()),
                Convert.ToHexString(sha256.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
