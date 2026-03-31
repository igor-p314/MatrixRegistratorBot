# Matrix Registrator Bot

Бот для регистрации пользователей в Matrix-сети через команды в чате.
Это нужно, чтобы избежать при регистрации использования электронной почты. 
Таким образом реализуется регистрация через "сарафанное радио", когда пользователи приглашают своих друзей в комнаты, а бот регистрирует их в MAS и отправляет учётные данные.
Работает по максимально наивному сценарию, без дополнительных проверок.

## Описание

Бот подключается к Matrix-серверу, отслеживает сообщения в комнатах и обрабатывает команды регистрации. При получении валидной команды создаёт нового пользователя в Matrix Authentication Service (MAS) и отправляет учётные данные пользователю в чат.

## Возможности

- Обработка команд регистрации: `!reg`, `!r`, `!register`, `!registr`, `!rgstr`
- Автоматическое создание пользователя через Matrix Authentication Service
- Генерация случайного пароля
- Отправка учётных данных пользователю в чат
- Принятие только прямых приглашений (1 на 1 без шифрования)
- Сохранение токена синхронизации для восстановления состояния
- Логирование через Serilog (консоль + файл)

## Требования

- .NET 10.0
- AOT-компиляция для оптимальной производительности
- Docker (опционально, для контейнеризации)
- Matrix Authentication Service (MAS)

## Переменные окружения

| Переменная | Описание |
|------------|----------|
| `MATRIX_HOMESERVER_URL` | URL homeserver'а (домен) |
| `MATRIX_BOT_USER_LOGIN` | Логин бота в Matrix |
| `MATRIX_BOT_USER_PASSWORD` | Пароль бота в Matrix |
| `MATRIX_BOT_BATCH_TOKEN_PATH` | Путь к файлу для сохранения токена синхронизации |
| `MATRIX_BOT_MAX_MESSAGE_AGE_MS` | Максимальный возраст сообщений для обработки (мс, по умолчанию 14400000) |
| `MATRIX_BOT_USER_TIMEOUT` | Таймаут для longpolling запросов ожидания сообщений (мс, по умолчанию 30000) |
| `MATRIX_BOT_ADMIN_BASIC_AUTH` | Basic авторизация для доступа к админке MAS |
| `MATRIX_REGISTRATION_ROOM_KEY` | Ключ комнаты для уведомлений о регистрациях |

## Использование

### Формат команд

```
!reg <имя_пользователя>
```

Имя пользователя должно соответствовать шаблону: `[a-z0-9._-]{3,64}`

### Пример

```
!reg testuser
```

Бот ответит:
- Логин: `testuser`
- Пароль: `<сгенерированный пароль>`

## Сборка и запуск

### Локальный запуск

```bash
dotnet run
```

### Сборка Native AOT

```bash
dotnet publish -c Release -r linux-musl-x64
```

### Docker

```bash
docker build -t matrix-registrator-bot .
docker run -d \
  -e MATRIX_BOT_USER_LOGIN=bot_login \
  -e MATRIX_BOT_USER_PASSWORD=bot_password \
  -e MATRIX_BOT_BATCH_TOKEN_PATH=/data/token.txt \
  -e MATRIX_HOMESERVER_URL=matrix.example.com \
  -e MATRIX_BOT_USER_TIMEOUT=30000 \
  -e MATRIX_BOT_ADMIN_BASIC_AUTH=admin_password \
  -e MATRIX_REGISTRATION_ROOM_KEY="!room:matrix.example.com" \
  -v /path/to/data:/data \
  matrix-registrator-bot
```

## Зависимости

- [Serilog](https://serilog.net/) — логирование
- [Polly](https://github.com/App-vNext/Polly) — обработка временных ошибок

## Лицензия

См. файл [LICENSE](LICENSE).

## Автор ридми
Qwen Code 0.13.1
