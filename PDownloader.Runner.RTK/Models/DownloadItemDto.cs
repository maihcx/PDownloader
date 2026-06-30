using System.Text.Json.Serialization;

namespace PDownloader.Runner.Models;

/// <summary>
/// Mirror of Core's DownloadItemDto — received via CFS "muxt-get-downloader-list"
/// and "muxt-download-progress" commands.
/// </summary>
public class DownloadItemDto
{
    [JsonPropertyName("id")]                 public string Id                 { get; set; } = string.Empty;
    [JsonPropertyName("url")]                public string Url                { get; set; } = string.Empty;
    [JsonPropertyName("fileName")]           public string FileName           { get; set; } = string.Empty;
    [JsonPropertyName("savePath")]           public string SavePath           { get; set; } = string.Empty;
    [JsonPropertyName("totalBytes")]         public long   TotalBytes         { get; set; }
    [JsonPropertyName("downloadedBytes")]    public long   DownloadedBytes    { get; set; }
    [JsonPropertyName("speedBps")]           public double SpeedBps           { get; set; }
    [JsonPropertyName("progress")]           public double Progress           { get; set; }
    [JsonPropertyName("status")]             public string Status             { get; set; } = string.Empty;
    [JsonPropertyName("statusText")]         public string StatusText         { get; set; } = string.Empty;
    [JsonPropertyName("speedFormatted")]     public string SpeedFormatted     { get; set; } = string.Empty;
    [JsonPropertyName("etaFormatted")]       public string EtaFormatted       { get; set; } = string.Empty;
    [JsonPropertyName("totalFormatted")]     public string TotalFormatted     { get; set; } = string.Empty;
    [JsonPropertyName("downloadedFormatted")]public string DownloadedFormatted{ get; set; } = string.Empty;
    [JsonPropertyName("errorMessage")]       public string ErrorMessage       { get; set; } = string.Empty;
    [JsonPropertyName("isActive")]           public bool   IsActive           { get; set; }
}
