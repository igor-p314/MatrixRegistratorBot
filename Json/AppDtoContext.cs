using MatrixRegistratorBot.Dto;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MatrixRegistratorBot.Json;

[JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(LoginResult))]
[JsonSerializable(typeof(AdminToken))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(SyncUpdate))]
[JsonSerializable(typeof(CreatedUserWrapper))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(bool))]
internal partial class AppDtoContext : JsonSerializerContext
{
}