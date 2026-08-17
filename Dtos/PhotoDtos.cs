namespace MeDan.Api.Dtos;

public record PhotoResponse
{
    public Guid Id { get; init; }
    public string Url { get; init; } = default!;
    public bool IsCover { get; init; }
    public int SortOrder { get; init; }
}
