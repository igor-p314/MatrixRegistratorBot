namespace MatrixRegistratorBot.Dto;

internal sealed record CreatedUserWrapper
{
    public required CreatedUser Data { get; set; }
}
