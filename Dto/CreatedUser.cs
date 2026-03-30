namespace MatrixRegistratorBot.Dto;

internal sealed record CreatedUserWrapper
{
    public required CreatedUser Data { get; set; }
}

internal sealed record CreatedUser
{
    public required string Id { get; set; }
}
