namespace LauncherBackend.Utilities;

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Models;
using Services;

/// <summary>
///   Handles downloading a file and calculating its hash at the same time
/// </summary>
public static class HashedFileDownloader
{
    public static async Task<string> DownloadAndHashFile(HttpClient httpClient, Uri downloadUrl, string fileToWriteTo,
        HashAlgorithm hasher, FilePrepareProgress operationProgress, ILogger logger,
        CancellationToken cancellationToken)
    {
        var parentFolder = Path.GetDirectoryName(fileToWriteTo);

        if (!string.IsNullOrEmpty(parentFolder))
            Directory.CreateDirectory(parentFolder);

        if (File.Exists(fileToWriteTo))
        {
            logger.LogInformation("Deleting file before re-downloading: {FileToWriteTo}", fileToWriteTo);
            File.Delete(fileToWriteTo);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var writer = File.OpenWrite(fileToWriteTo);

        string hash;
        try
        {
            var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            // Check that we don't accidentally try to unzip a html response
            if (response.Headers.TryGetValues("Content-Type", out var contentTypeValues))
            {
                var contentType = contentTypeValues.FirstOrDefault();

                if (contentType != null)
                {
                    if (contentType.Contains("text") || contentType.Contains("html"))
                    {
                        throw new Exception(
                            $"Expected non-text (and non-html) response from server, got type: {contentType}");
                    }
                }
            }

            var length = response.Content.Headers.ContentLength;

            long downloadedBytes = 0;
            operationProgress.CurrentProgress = downloadedBytes;

            if (length != null)
            {
                operationProgress.FinishedProgress = length;
            }
            else
            {
                logger.LogWarning("Didn't get Content-Length header so can't show download progress");
            }

            var reader = await response.Content.ReadAsStreamAsync(cancellationToken);

            var buffer = new byte[LauncherConstants.DownloadBufferSize];

            // Pass the data simultaneously to the hasher and the file to speed things up
            while (true)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                if (read <= 0)
                    break;

                downloadedBytes += read;

                var writeTask = writer.WriteAsync(buffer, 0, read, cancellationToken);

                hasher.TransformBlock(buffer, 0, read, null, 0);
                await writeTask;

                // TODO: do we need some kind of rate limit on the progress updates?
                operationProgress.CurrentProgress = downloadedBytes;
            }

            // Finalize the hasher state
            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            hash = SharedBase.Utilities.FileUtilities.HashToHex(hasher.Hash ??
                throw new Exception("Hasher didn't calculate hash"));

            logger.LogDebug("Downloaded {DownloadedBytes} bytes with hash of: {Hash}", downloadedBytes, hash);

            if (length != null && downloadedBytes != length)
            {
                logger.LogWarning(
                    "Downloaded bytes doesn't match the Content-Length header {Length} != {DownloadedBytes}", length,
                    downloadedBytes);
            }
        }
        catch (Exception)
        {
            try
            {
                writer.Close();
                File.Delete(fileToWriteTo);
            }
            catch (Exception e2)
            {
                logger.LogWarning(e2, "Failed to remove failed to download file");
            }

            throw;
        }
        finally
        {
            try
            {
                writer.Close();
            }
            catch (Exception e2)
            {
                // This catch is here mostly so that in the error case where the file needs to be closed early for
                // deleting, this doesn't cause an issue
                logger.LogWarning(e2, "Failed to close temp file writer");
            }
        }

        return hash;
    }
}
