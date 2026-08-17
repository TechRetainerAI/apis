namespace MeDan.Api.Services;

/// <summary>Bound from the "Push" configuration section.</summary>
public class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>
    /// Firebase project id — <c>medan-6bca0</c>. Part of the FCM v1 send URL.
    /// </summary>
    public string ProjectId { get; set; } = "medan-6bca0";

    /// <summary>
    /// Path to the Firebase service-account JSON used to mint access tokens.
    /// Keep it out of source control — set via user-secrets or the
    /// <c>Push__ServiceAccountPath</c> environment variable.
    ///
    /// Alternatively leave this empty and put the JSON itself in
    /// <see cref="ServiceAccountJson"/>, which suits container deployments
    /// where mounting a file is awkward.
    ///
    /// When neither is set the API logs what it *would* have sent instead of
    /// calling FCM, so the rest of the flow stays testable.
    /// </summary>
    public string? ServiceAccountPath { get; set; }

    /// <summary>Raw service-account JSON, as an alternative to a file path.</summary>
    public string? ServiceAccountJson { get; set; }
}
