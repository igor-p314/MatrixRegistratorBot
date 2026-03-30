using MatrixRegistratorBot.Dto;
using MatrixRegistratorBot.Matrix;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MatrixRegistratorBot;

internal partial class MatrixService
{
    internal static readonly string[] RegistrationCommands = ["!reg", "!r", "!register", "!registr", "!rgstr"];

    private static readonly Regex userNameRules = CreateUserNameRegex();
    private readonly int _maxMessageAge = 14400000; // 4 hours in milliseconds
    private readonly TokenService _tokenService = new();
    private readonly TimeProvider _timeProvider;
    private readonly HttpService _httpService = new();

    public MatrixService()
    {
        var tempString = Environment.GetEnvironmentVariable("MATRIX_BOT_MAX_MESSAGE_AGE_MS");
        if (!string.IsNullOrEmpty(tempString))
        {
            _maxMessageAge = int.Parse(tempString);
        }

        _timeProvider = TimeProvider.System;
    }

    internal async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var authorizationService = await _httpService.AuthorizeAsync("/_matrix/client/v3/login", cancellationToken).ConfigureAwait(false);
        await ConnectToServerAsync(authorizationService, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ConnectToServerAsync(AuthorizationService authorizationService, CancellationToken cancellationToken)
    {
        string? batchFromFile = await _tokenService.GetAsync(cancellationToken).ConfigureAwait(false);
        string url;
        string nextBatch;
        if (string.IsNullOrEmpty(batchFromFile))
        {
            url = "/_matrix/client/v3/sync";
        }
        else
        {
            nextBatch = batchFromFile;
            url = $"/_matrix/client/v3/sync?since={Uri.EscapeDataString(nextBatch)}&timeout={_httpService.TimeOutMilliseconds}";
        }

        var response = await _httpService.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        nextBatch = await ProcessSyncDataResponseAsync(response, authorizationService.UserId, cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested && !string.IsNullOrEmpty(nextBatch))
        {
            try
            {
                await _tokenService.SaveAsync(nextBatch, cancellationToken).ConfigureAwait(false);
                url = $"/_matrix/client/v3/sync?since={Uri.EscapeDataString(nextBatch)}&timeout={_httpService.TimeOutMilliseconds}";
                response = await _httpService.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                nextBatch = await ProcessSyncDataResponseAsync(response, authorizationService.UserId, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // this is fine
            }
        }

        Log.Information("Disconnected from matrix.");
    }

    private async ValueTask<string> ProcessSyncDataResponseAsync(string response, string currentUserId, CancellationToken cancellationToken)
    {
        var syncData = JsonSerializer.Deserialize(response, Json.AppDtoContext.Default.SyncUpdate)
            ?? throw new InvalidOperationException("Failed to deserialize sync data.");

        if (syncData.Rooms is not null)
        {
            if (syncData.Rooms.Invite.Count > 0)
            {
                await ProcessInvitesAsync(syncData.Rooms.Invite, cancellationToken).ConfigureAwait(false);
            }

            if (syncData.Rooms.Join.Count > 0)
            {
                var messages = syncData.Rooms.Join
                    .Where(r => r.Value.Timeline?.Events.Count > 0)
                    .SelectMany(r => r.Value.Timeline!.Events
                        .Where(e => e.Type == "m.room.message"
                                && !string.IsNullOrEmpty(e.Content.Body)
                                && !currentUserId.Equals(e.Sender, StringComparison.OrdinalIgnoreCase)
                                && _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() - e.OriginServerTs < _maxMessageAge)
                    .Select(e => (RoomKey: r.Key, Text: e.Content.Body!)));

                foreach (var (RoomKey, Text) in messages)
                {
                    if (RegistrationCommands.Any(Text.StartsWith))
                    {
                        await ProcessRegistrationCommandAsync(RoomKey, Text, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await RespondWrongCommandAsync(RoomKey, cancellationToken).ConfigureAwait(false);
                    }
                }

                foreach (var room in syncData.Rooms.Join)
                {
                    var lastEvent = room.Value.Timeline?.Events
                        .OrderByDescending(e => e.OriginServerTs)
                        .FirstOrDefault();
                    if (lastEvent?.EventId is not null)
                    {
                        await SetReadMarkerAsync(room.Key, lastEvent.EventId, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        return syncData.NextBatch ?? throw new InvalidOperationException("Sync data does not contain next batch token.");
    }

    private ValueTask ProcessInvitesAsync(Dictionary<string, InviteData> invites, CancellationToken cancellationToken)
    {
        foreach (var invite in invites)
        {
            var membersCount = invite.Value.InviteState.Events.Count(e => e.Type == "m.room.member");
            var roomName = invite.Value.InviteState.Events.FirstOrDefault(e => e.Type == "m.room.name")?.Content?.Name ?? "Unknown";
            var isEncrypted = invite.Value.InviteState.Events.Any(e => e.Type == "m.room.encryption");
            if (membersCount == 2 && !isEncrypted) // only direct
            {
                Task.Run(() => JoinDirectRoomAsync(invite.Key, cancellationToken));
            }
            else
            {
                Task.Run(() => LeaveRoomAsync(invite.Key, cancellationToken));
                Log.Information("Отклонено приглашение в комнату '{roomName}'. Количество участников: {membersCount}, IsEncrypted = {isEncrypted}.", roomName, membersCount, isEncrypted);
            }
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask JoinDirectRoomAsync(string roomKey, CancellationToken cancellationToken)
    {
        var joinUrl = $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomKey)}/join";
        var response = await _httpService.PostAsync(joinUrl, null, cancellationToken).ConfigureAwait(false);
        Log.Information("Вход в комнату {roomKey}: {statusCode}", roomKey, response.StatusCode);
    }

    private async ValueTask LeaveRoomAsync(string roomKey, CancellationToken cancellationToken)
    {
        var leaveUrl = $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomKey)}/leave";
        var response = await _httpService.PostAsync(leaveUrl, null, cancellationToken).ConfigureAwait(false);
        Log.Information("Уход из комнаты {roomKey}: {statusCode}", roomKey, response.StatusCode);
    }

    private ValueTask RespondWrongCommandAsync(string roomKey, CancellationToken cancellationToken)
    {
        return SendToRoomAsync(roomKey, Messages.RegisterHelpMessage, cancellationToken);
    }

    private async ValueTask SendToRoomAsync(string roomKey, Message message, CancellationToken cancellationToken)
    {
        var leaveUrl = $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomKey)}/send/m.room.message";

        var content = JsonContent.Create(message.ToSerializableMessage(), Json.AppDtoContext.Default.DictionaryStringString);
        var response = await _httpService.PostAsync(leaveUrl, content, cancellationToken).ConfigureAwait(false);

        Log.Information("Ответ отправлен в комнату {roomKey}: '{message}' {statusCode}", roomKey, message, response.StatusCode);
    }

    private async ValueTask ProcessRegistrationCommandAsync(string roomKey, string message, CancellationToken cancellationToken)
    {
        var userName = ParseUsername(message);
        if (!string.IsNullOrEmpty(userName))
        {
            await ProcessRegistrationAsync(roomKey, userName, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RespondWrongCommandAsync(roomKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ProcessRegistrationAsync(string roomKey, string userNameToRegister, CancellationToken cancellationToken)
    {
        var token = await _httpService.AuthorizeAdminAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            Log.Information("Успешная авторизация в MAS");

            var userId = await CreateUserAsync(roomKey, userNameToRegister, token, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(userId))
            {
                var password = CreatePassword();
                await SetPasswordAsync(roomKey, userNameToRegister, userId, password, token, cancellationToken).ConfigureAwait(false);
            }
        }

    }

    private async ValueTask<string?> CreateUserAsync(string roomKey, string userNameToRegister, string token, CancellationToken cancellationToken)
    {
        string? result = null;
        var createResult = await _httpService.CreateUserAsync(userNameToRegister, token, cancellationToken).ConfigureAwait(false);
        switch (createResult.StatusCode)
        {
            case System.Net.HttpStatusCode.Created:
                result = createResult.Id;

                Log.Information("Пользователь создан '{UserName}' with id '{UserId}'", createResult.UserName, createResult.Id);
                break;

            case System.Net.HttpStatusCode.Conflict:
                await SendToRoomAsync(roomKey, new Message($"Логин '{createResult.UserName}' уже занят. Придумайте другой."), cancellationToken).ConfigureAwait(false);
                break;

            default:
                Log.Error("Неизвестный статус при создани пользователя: {StatusCode}", createResult.StatusCode);
                break;
        }

        return result;
    }

    private async ValueTask SetPasswordAsync(string roomKey, string userName, string userId, string password, string token, CancellationToken cancellationToken)
    {
        var result = await _httpService.SetUserPasswordAsync(userId, password, token, cancellationToken).ConfigureAwait(false);

        switch (result.StatusCode)
        {
            case System.Net.HttpStatusCode.OK:
            case System.Net.HttpStatusCode.NoContent:
                Log.Information("Пароль успешно установлен для пользователя {userId}", userId);
                
                var newUserId = $"@{userName}:{Environment.GetEnvironmentVariable("MATRIX_HOMESERVER_URL")}";
                await SendToRoomAsync(roomKey, new FormattedMessage($"Пользователь <a href=\"https://matrix.to/#/@{newUserId}\">{newUserId}</a> успешно создан. Логин:", $"Пользователь {userName} успешно создан. Логин:"), cancellationToken).ConfigureAwait(false);
                await SendToRoomAsync(roomKey, new Message(userName), cancellationToken).ConfigureAwait(false);
                await SendToRoomAsync(roomKey, new Message("Пароль:"), cancellationToken).ConfigureAwait(false);
                await SendToRoomAsync(roomKey, new Message(password), cancellationToken).ConfigureAwait(false);

                break;

            case System.Net.HttpStatusCode.BadRequest:
                await SendToRoomAsync(roomKey, new Message("Ошибка задания пароля. Обратитесь к администратору."), cancellationToken).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private async ValueTask SetReadMarkerAsync(string roomKey, string eventId, CancellationToken cancellationToken)
    {
        var url = $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomKey)}/read_markers";

        var content = JsonContent.Create(new Dictionary<string, string>
        {
            { "m.fully_read", eventId },
            { "m.read", eventId }
        },
        Json.AppDtoContext.Default.DictionaryStringString);

        await _httpService.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
    }

    private static string? ParseUsername(string message)
    {
        string? result = null;
        var parts = message.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2
            && RegistrationCommands.Contains(parts[0])
            && userNameRules.IsMatch(parts[1]))
        {
            result = parts[1].Trim();
        }

        return result;
    }

    private static string CreatePassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-+=<>?";
        var random = new Random();
        var passwordChars = Enumerable.Range(0, 16)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray();
        return new string(passwordChars);
    }

    [GeneratedRegex(@"[a-z0-9._-]{3,64}")]
    private static partial Regex CreateUserNameRegex();
}
