using System.Collections.Generic;

namespace MatrixRegistratorBot.Matrix;

internal record Message
{
    protected readonly string _messageText;

    public Message(string messageText)
    {
        _messageText = messageText;
    }

    public virtual Dictionary<string, string> ToSerializableMessage()
    {
        return new Dictionary<string, string>
        {
            { "msgtype", "m.text" },
            { "body", _messageText },
        };
    }
}
