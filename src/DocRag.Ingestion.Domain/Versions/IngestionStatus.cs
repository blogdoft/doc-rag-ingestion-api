namespace DocRag.Ingestion.Domain.Versions;

/// <summary>Reception state of a version's original file; unrelated to conversion or indexing.</summary>
public enum IngestionStatus
{
    /// <summary>The version exists and no file has been received.</summary>
    AwaitingUpload,

    /// <summary>A file is being received.</summary>
    Uploading,

    /// <summary>The original file was stored and registered. Terminal for this version.</summary>
    Uploaded,

    /// <summary>The last upload attempt failed; a new attempt is allowed.</summary>
    UploadFailed,
}
