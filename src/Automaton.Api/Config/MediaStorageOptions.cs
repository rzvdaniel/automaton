namespace Automaton.Api.Config;

public class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";

    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    public long DefaultQuotaBytes { get; set; } = 250 * 1024 * 1024;
}
