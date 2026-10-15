using System.ComponentModel.DataAnnotations;

namespace DocRag.Ingestion.Infrastructure.Garage;

/// <summary>Garage (S3 API) settings, bound from the <c>Garage</c> configuration section.</summary>
public sealed class GarageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Garage";

    /// <summary>Gets or sets the S3 endpoint URL.</summary>
    [Required]
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the S3 region name.</summary>
    [Required]
    public string Region { get; set; } = "garage";

    /// <summary>Gets or sets the bucket holding original files; never created implicitly.</summary>
    [Required]
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Gets or sets the access key.</summary>
    [Required]
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the secret key.</summary>
    [Required]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether path-style addressing is used.</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Gets or sets the multipart part size in MiB (5 to 512). Memory per upload is about one part.</summary>
    [Range(5, 512)]
    public int PartSizeMiB { get; set; } = 8;
}
